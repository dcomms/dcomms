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

        void ProcessUdpPacket(UdpClient udpSocket, IPEndPoint remoteEndpoint, byte[] payloadData)
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

        UniqueDataTracker _uniquePowDataForCurrentPeriod = new UniqueDataTracker();
        UniqueDataTracker _uniquePowDataForPreviousPeriod = new UniqueDataTracker();

        bool PassStatelessPoWfilter(UdpClient udpSocket, IPEndPoint remoteEndpoint, ClientHelloPacket0 packet) // sends responses 
        {
            switch (packet.StatelessProofOfWorkType)
            {
                case StatelessProofOfWorkType._2019_06:
                    // verify size of PoW data
                    if (packet.StatelessProofOfWorkData.Length < 8 || packet.StatelessProofOfWorkData.Length > 64)
                        throw new CcpBadPacketException();

                    // verify datetime ("period")
                    // return err code if time is wrong, with correct server's UTC time
                    uint receivedTimeSec32;
                    unsafe
                    {
                        fixed (byte* statelessProofOfWorkDataPtr = packet.StatelessProofOfWorkData)
                        {
                            receivedTimeSec32 = *((uint*)statelessProofOfWorkDataPtr);
                        }
                    }
                    var localTimeSec32 = TimeSec32UTC;
                    var diffSec = Math.Abs((int)unchecked(localTimeSec32 - receivedTimeSec32));
                    if (diffSec > 3600)
                    {
                        // respond with error "try again with valid clock"
                        RespondToHello0(udpSocket, remoteEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadClock, packet.ClientHelloToken);
                        return false;
                    }

                    var hash = _cryptoLibrary.GetHashSHA256(packet.OriginalPacketPayload);
                    // calculate hash, considering entire packet data (including stateless PoW result)
                    // verify hash result
                    if (hash[4] != 7 || hash[5] != 7 || hash[6] != 7)
                    {
                        HandleBadStatelessPowPacket(remoteEndpoint);
                        return false;
                    }

                    // pass the hash to UniqueDataTracker  of current period or previous period (if it is still "previous persiod" at client)
                    if (_uniquePowDataForCurrentPeriod.TryInputData(hash))
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


    }
    /// <summary>
    /// element of PoW validation (anti-DDoS) subsystem
    /// stores hashes, results of SHA, previous N _valid_ PoW's
    /// provides a fast routine that checks "if the new valid PoW unique?", and this routine can return false positives (intentionally designed, to get fastest performance)
    /// contains the unique quadruples (dwords)   for previous "time unit" ("10min") (when new "time unit" comes, this container becomes reset)
    /// 
    /// thread-unsafe
    /// 
    /// server can check 400K hashes per sec
    /// client can send unique PoW every 200ms (average)
    /// keep previous N valid requests (hashes)
    /// </summary>
    public class UniqueDataTracker
    {
        /// <summary>
        /// consider loop for every group of 4 bytes in input data: bytes A,B,C,D  (quadruple, double word, DWORD)
        /// if the ABCD value exists in this container, a bit is set to 1 at element index [A*65536+B*256+C], bit index [D mod 8]
        /// where 5 bits of D are ignored (they can be non-unique in this container)
        /// </summary>
        byte[] _previousDwordFlagBits = new byte[256 * 256 * 256]; // takes 16MB of RAM, not too big for modern devices

        const int MaxElementsInPreviousInputData = 65536 * 8; // max 16 MB
        Queue<byte[]> _previousInputData = new Queue<byte[]>(); // first input last output
        
        public void Reset()
        {
            _previousDwordFlagBits.Initialize();
            _previousInputData.Clear();
        }
        public unsafe bool TryInputData(byte[] inputData)
        {
            if (inputData == null) throw new ArgumentNullException(nameof(inputData));
            if (inputData.Length % 4 != 0) throw new ArgumentException(nameof(inputData)); // must be of size N*4

            int numberOfDwords = inputData.Length << 2;

            fixed (byte *previousDwordFlagBitsPtr = _previousDwordFlagBits)
            {
                fixed (byte* inputDataPtr = inputData)
                {
                    uint* inputDataPtr32 = (uint*)inputDataPtr;
                    for (int i = 0; i < numberOfDwords; i++, inputDataPtr32++)
                    {
                        uint dword = *inputDataPtr32;
                        uint previousDwordFlagsIndex = dword & 0x00FFFFFF;
                        var previousDwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                        var previousDwordFlagBitMask = (byte)(1 << previousDwordFlagBitIndex);
                        byte *previousDwordFlagBits = previousDwordFlagBitsPtr + previousDwordFlagsIndex;
                        if (((*previousDwordFlagBits) & previousDwordFlagBitMask) != 0)
                        { // DWORD is not unique
                            { // unset flags for previously enumerated dword's (mark as "unused") , i.e. clean bits set in current procedure call                               
                                for (int j = i; ;)
                                {
                                    j--;
                                    if (j < 0) break;
                                    inputDataPtr32--;
                                                                       
                                    dword = *inputDataPtr32;
                                    previousDwordFlagsIndex = dword & 0x00FFFFFF;
                                    previousDwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                                    previousDwordFlagBitMask = (byte)(1 << previousDwordFlagBitIndex);                                                                       

                                    previousDwordFlagBits = previousDwordFlagBitsPtr + previousDwordFlagsIndex;

                                    // set bit back to 0
                                    *previousDwordFlagBits &= (byte)(~previousDwordFlagBitMask);
                                }
                            }
                            return false;
                        }
                        *previousDwordFlagBits |= previousDwordFlagBitMask; // mark this dword as "used"
                    }
                }
            }
                       
            _previousInputData.Enqueue(inputData);

            if (_previousInputData.Count > MaxElementsInPreviousInputData)
            {
                var oldInputData = _previousInputData.Dequeue();
                numberOfDwords = oldInputData.Length << 2;
                fixed (byte* previousDwordFlagBitsPtr = _previousDwordFlagBits)
                {
                    fixed (byte* oldInputDataPtr = oldInputData)
                    {
                        uint* oldInputDataPtr32 = (uint*)oldInputDataPtr;
                        for (int i = 0; i < numberOfDwords; i++, oldInputDataPtr32++)
                        {
                            uint dword = *oldInputDataPtr32;
                            uint previousDwordFlagsIndex = dword & 0x00FFFFFF;
                            var previousDwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                            var previousDwordFlagBitMask = (byte)(1 << previousDwordFlagBitIndex);
                            byte* previousDwordFlagBits = previousDwordFlagBitsPtr + previousDwordFlagsIndex;                           
                            *previousDwordFlagBits |= (byte)(~previousDwordFlagBitMask); // mark this dword as "unused"
                        }
                    }
                }
            }

            return true;
        }
    }

    class CcpBadPacketException: Exception
    {

    }

    enum CcpSecurityLevel
    {

    }
}
