using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dcomms.CCP
{
    class CcpServer
    {
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + _stopwatch.Elapsed; } }
        uint TimeSec32UTC => MiscProcedures.DateTimeToUint32(DateTimeNowUtc);
        readonly ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        readonly UniqueDataTracker _recentUniquePowData;

        public CcpServer()
        {
            _recentUniquePowData = new UniqueDataTracker(TimeSec32UTC);
        }


        void ProcessUdpPacket(UdpClient udpSocket, IPEndPoint remoteEndpoint, byte[] payloadData) // multi-threaded, receiver threads
        {
            try
            {
                var reader = PacketProcedures.CreateBinaryReader(payloadData, 0);
                var packetType = (CcpPacketType)reader.ReadByte();
                switch (packetType)
                {
                    case CcpPacketType.ClientHelloPacket0:
                        ProcessClientHello0(udpSocket, remoteEndpoint, reader, payloadData);
                        break;
                    default:
                        throw new CcpBadPacketException();
                }
            }
            catch (Exception exc)
            {
                HandleMalformedPacket(remoteEndpoint);
                RespondToHello0(udpSocket, remoteEndpoint, ServerHello0Status.ErrorBadPacket, null);
            }
        }
        void RespondToHello0(UdpClient udpSocket, IPEndPoint remoteEndpoint, ServerHello0Status status, byte[] clientHelloToken)
        {
            var response = new ServerHelloPacket0 { Status = status, ClientHelloToken = clientHelloToken };
            var responseBytes = response.Encode();
            udpSocket.Send(responseBytes, responseBytes.Length, remoteEndpoint);
        }
        void HandleMalformedPacket(IPEndPoint remoteEndpoint)
        {
            //todo
        }
        void HandleBadStatelessPowPacket(IPEndPoint remoteEndpoint)
        {
            //todo
        }
        void ProcessClientHello0(UdpClient udpSocket, IPEndPoint remoteEndpoint, BinaryReader reader, byte[] payloadData)
        {
            var packet = new ClientHelloPacket0(reader, payloadData);
            if (!PassStatelessPoWfilter(udpSocket, remoteEndpoint, packet))
                return;
                       
            ////todo
        }

        bool PassStatelessPoWfilter(UdpClient udpSocket, IPEndPoint remoteEndpoint, ClientHelloPacket0 packet) // sends responses 
        {
            switch (packet.StatelessProofOfWorkType)
            {
                case StatelessProofOfWorkType._2019_06:
                    // verify size of PoW data
                    if (packet.StatelessProofOfWorkData.Length < 12 || packet.StatelessProofOfWorkData.Length > 64)
                        throw new CcpBadPacketException();

                    // verify datetime ("period")
                    // return err code if time is wrong, with correct server's UTC time
                    uint receivedTimeSec32;
                
                    unsafe
                    {
                        fixed (byte* statelessProofOfWorkDataPtr = packet.StatelessProofOfWorkData)
                        {
                            fixed (byte* addressBytesPtr = remoteEndpoint.Address.GetAddressBytes())
                            {
                                receivedTimeSec32 = *((uint*)statelessProofOfWorkDataPtr);                                
                                if (addressBytesPtr[0] != statelessProofOfWorkDataPtr[4] ||
                                    addressBytesPtr[1] != statelessProofOfWorkDataPtr[5] ||
                                    addressBytesPtr[2] != statelessProofOfWorkDataPtr[6] ||
                                    addressBytesPtr[3] != statelessProofOfWorkDataPtr[7]
                                    )
                                {
                                    RespondToHello0(udpSocket, remoteEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadSourceIp, packet.ClientHelloToken);
                                    return false;
                                }
                            }
                        }
                    }


                    var localTimeSec32 = TimeSec32UTC;
                    var diffSec = Math.Abs((int)unchecked(localTimeSec32 - receivedTimeSec32));
                    if (diffSec > CcpServerLogic.StatelessPoW_MaxClockDifferenceS)
                    {
                        // respond with error "try again with valid clock" - legitimate user has to get valid clock from some time server and synchronize itself with the server
                        RespondToHello0(udpSocket, remoteEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadClock, packet.ClientHelloToken);
                        return false;
                    }

                    var hash = _cryptoLibrary.GetHashSHA256(packet.OriginalPacketPayload);
                    // calculate hash, considering entire packet data (including stateless PoW result)
                    // verify hash result
                    if (!StatelessPowHashIsOK(hash))
                    {
                        HandleBadStatelessPowPacket(remoteEndpoint);
                        return false;
                    }


                   

                    // check if hash is unique
                    bool dataIsUnique;
                    lock (_recentUniquePowData)
                    {                        
                        dataIsUnique = _recentUniquePowData.TryInputData(hash, localTimeSec32);
                    }

                    if (dataIsUnique)
                    {
                        return true;
                    }
                    else
                    {
                        // respond with error "try again with unique PoW data"
                        RespondToHello0(udpSocket, remoteEndpoint, ServerHello0Status.ErrorTryAgainRightNowWithThisServer, packet.ClientHelloToken);
                        return false;
                    }
                default:
                    throw new CcpBadPacketException();
            }
        }
        internal static bool StatelessPowHashIsOK(byte[] hash)
        {
            if (hash[4] != 7 || hash[5] != 7
                || hash[6] > 100
                )
                return false;
            else return true;
        }
    }

    class CcpServerLogic
    {
        public const int StatelessPoW_MaxClockDifferenceS = 20 * 60;
        public const int StatelessPoW_RecentUniqueDataResetPeriodS = 10 * 60;
    }

    /// <summary>
    /// element of PoW validation (anti-DDoS) subsystem
    /// 
    /// still possible DoS attack#1: attacker precalculates hashes for some [future] period and sends a burst of hello0 packets, and for this time there will be 
    /// LESS unique (free) hashes for legitimate users
    /// 
    /// still possible attack #2: attacker re-sends previously sent valid PoW
    /// 
    /// stores hashes, results of SHA, previous N _valid_ PoW's
    /// provides a fast routine that checks "if the new valid PoW unique?", and this routine can return false positives (intentionally designed, to get fastest performance)
    /// contains the unique quadruples (dwords)   for previous "time unit" ("10min") (when new "time unit" comes, this container becomes reset)
    /// 
    /// thread-unsafe
    /// 
    /// server can check 400K hashes per sec
    /// client can send unique PoW every 200ms (average)
    /// keep previous valid requests (hashes) for periodK
    /// </summary>
    public class UniqueDataTracker
    {
        /// <summary>
        /// consider loop for every group of 4 bytes in input data: bytes A,B,C,D  (quadruple, double word, DWORD)
        /// if the ABCD value exists in this container, a bit is set to 1 at element index [A*65536+B*256+C], bit index [D mod 8]
        /// where 5 bits of D are ignored (they can be non-unique in this container)
        ///  
        /// having capacity of 256**4 = 4.3E9 unique values, it can accept 7.15M unique values per second
        /// takes 16MB of RAM, not too big for modern devices, but 
        /// 
        /// against (precalculated) attack #1: if reset period is 10 minutes,
        /// then for a 10-minute-duration attack it needs 4.3E9 valid and unique PoW values. if it takes 300ms to calculate PoW, 
        /// it requires 1.3E9 seconds of single-core CPU time = 466 days using 32-core CPU
        /// 
        /// counter-measure against attack #1: if server sees that it is under attack (too many unique values filled) - then it automatically resets the unique values
        /// 
        /// </summary>
        byte[] _dwordFlagBits = new byte[256 * 256 * 256];
        uint _uniqueValuesCount, _uniqueValuesOverflowCount;
        bool UniqueValuesCapacityOverflowFlag => _uniqueValuesCount > _uniqueValuesOverflowCount;
        
        uint _latestResetTimeSec32UTC;
        public UniqueDataTracker(uint timeSec32UTC)
        {
            _uniqueValuesOverflowCount = (uint)((double)_dwordFlagBits.Length * 256 * 0.3);
            Reset(timeSec32UTC, false);
        }
        void Reset(uint timeSec32UTC, bool resetDwordFlagBits = true)
        {
            _latestResetTimeSec32UTC = timeSec32UTC;
            if (resetDwordFlagBits) _dwordFlagBits.Initialize();
            _uniqueValuesCount = 0;
        }
        public unsafe bool TryInputData(byte[] inputData, uint timeSec32UTC)
        {
            if (inputData == null) throw new ArgumentNullException(nameof(inputData));
            if (inputData.Length % 4 != 0) throw new ArgumentException(nameof(inputData)); // must be of size N*4
            
            if (unchecked(timeSec32UTC - _latestResetTimeSec32UTC) > CcpServerLogic.StatelessPoW_RecentUniqueDataResetPeriodS)
                Reset(timeSec32UTC);
            
            int numberOfDwords = inputData.Length << 2;

            fixed (byte *dwordFlagBitsPtr = _dwordFlagBits)
            {
                fixed (byte* inputDataPtr = inputData)
                {
                    uint* inputDataPtr32 = (uint*)inputDataPtr;
                    for (int i = 0; i < numberOfDwords; i++, inputDataPtr32++)
                    {
                        uint dword = *inputDataPtr32;
                        uint dwordFlagsIndex = dword & 0x00FFFFFF;
                        var dwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                        var dwordFlagBitMask = (byte)(1 << dwordFlagBitIndex);
                        byte *dwordFlagBits = dwordFlagBitsPtr + dwordFlagsIndex;
                        if (((*dwordFlagBits) & dwordFlagBitMask) != 0)
                        { // DWORD is not unique
                            { // unset flags for previously enumerated dword's (mark as "unused") , i.e. clean bits set in current procedure call                               
                                for (int j = i; ;)
                                {
                                    j--;
                                    if (j < 0) break;
                                    inputDataPtr32--;
                                                                       
                                    dword = *inputDataPtr32;
                                    dwordFlagsIndex = dword & 0x00FFFFFF;
                                    dwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                                    dwordFlagBitMask = (byte)(1 << dwordFlagBitIndex);                                                                       

                                    dwordFlagBits = dwordFlagBitsPtr + dwordFlagsIndex;

                                    // set bit back to 0
                                    *dwordFlagBits &= (byte)(~dwordFlagBitMask);
                                }
                            }
                            return false;
                        }
                        *dwordFlagBits |= dwordFlagBitMask; // mark this dword as "used"
                    }
                }
            }

            _uniqueValuesCount++;
            if (UniqueValuesCapacityOverflowFlag)
            {
                // counter-measure against attack #1: if server sees that it is under attack (too many unique values filled) - then it automatically resets the unique values
                Reset(timeSec32UTC);
            }
            return true;
        }
    }

    class CcpBadPacketException: Exception
    {
    }

    class CcpServerSideSession
    {
        uint LatestActivityTime32S { get; set; } // to remove it on timeout
        byte[] ServerSessionToken;
        byte[] ClientHelloToken;

        IPEndPoint ClientEndpoint { get; set; }
        
        StatefulProofOfWorkType PowType { get; set; }
        byte[] PoWrequestData { get; set; } // pow for ping request, against stateful DoS attacks
    }

}
