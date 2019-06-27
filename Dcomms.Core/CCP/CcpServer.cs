using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dcomms.CCP
{
    class CcpServer: IDisposable, ICcpTransportUser
    {
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + _stopwatch.Elapsed; } }
        uint TimeSec32UTC => MiscProcedures.DateTimeToUint32(DateTimeNowUtc);
        readonly ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        readonly CcpServerConfiguration _config;
        ICcpTransport _ccpTransport;
        #region packet processor thread
        Thread _packetProcessorThread;
        bool _disposing;
        class ReceivedPacket
        {
            public byte[] Data;
            public ICcpRemoteEndpoint ClientEndpoint;
        }
        readonly Queue<ReceivedPacket> _packetsToProcess = new Queue<ReceivedPacket>(); // locked
        #endregion
        #region state
        readonly UniqueDataTracker _recentUniquePowData; // accessed by processor thread only
        readonly Snonce0Table _snonce0Table; // accessed by processor thread only
        #endregion
        public CcpServer(CcpServerConfiguration config)
        {
            _config = config;
            _recentUniquePowData = new UniqueDataTracker(TimeSec32UTC, _config);
            _snonce0Table = new Snonce0Table(TimeSec32UTC, _config);
            _ccpTransport = new CcpUdpTransport(this, _config.CcpUdpLocalServerPort);
            _packetProcessorThread = new Thread(PacketProcessorThreadEntry);
            _packetProcessorThread.Name = "CCP server processor";
            _packetProcessorThread.Start();
        }
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;
            _packetProcessorThread.Join();
            _ccpTransport.Dispose();
        }
        #region packets processing, general
        void ICcpTransportUser.ProcessPacket(ICcpRemoteEndpoint remoteEndpoint, byte[] data) // receiver thread(s)
        {
            lock (_packetsToProcess)
            {
                if (_packetsToProcess.Count > _config.PacketProcessingQueueMaxCount)
                {
                    OnPacketProcessingQueueOverloaded();
                    return;
                }
                _packetsToProcess.Enqueue(new ReceivedPacket { ClientEndpoint = remoteEndpoint, Data = data });
            }
        }
        void PacketProcessorThreadEntry()
        {
            while (!_disposing)
            {
                try
                {
                    for (;;)
                        if (!PacketProcessorThreadProcedure())
                            break;
                }              
                catch (Exception exc)
                {
                    HandleExceptionInPacketProcessorThread(exc);
                }
                Thread.Sleep(10);
            }
        }
        /// <returns>true if there are more packets in queue, and need to call this procedure again</returns>
        bool PacketProcessorThreadProcedure()
        {
            ReceivedPacket p;
            lock (_packetsToProcess)
            {
                if (_packetsToProcess.Count == 0) return false;
                p = _packetsToProcess.Dequeue();                        
            }

            try
            {
                var reader = PacketProcedures.CreateBinaryReader(p.Data, 0);
                var packetType = (CcpPacketType)reader.ReadByte();
                switch (packetType)
                {
                    case CcpPacketType.ClientHelloPacket0:
                        ProcessClientHello0(p.ClientEndpoint, reader, p.Data);
                        break;
                    case CcpPacketType.ClientHelloPacket1:
                        ProcessClientHello1(p.ClientEndpoint, reader, p.Data);
                        break;
                    default:
                        HandleMalformedPacket(p.ClientEndpoint);
                        break;
                }
            }
            catch (Exception exc)
            {
                HandleExceptionInPacketProcessorThread(exc);
                HandleMalformedPacket(p.ClientEndpoint);
                if (_config.RespondErrors) RespondToHello0(p.ClientEndpoint, ServerHello0Status.ErrorBadPacket, null);
            }

            return true;
        }
        #endregion

        #region error handlers
        void OnPacketProcessingQueueOverloaded()
        {//todo
        }
        void HandleExceptionInPacketProcessorThread(Exception exc)
        {// todo
        }
        void ICcpTransportUser.HandleExceptionInCcpReceiverThread(Exception exc)
        {// todo
        }
        /// <summary>
        /// possibly but not neccessarily malformed, because it created an exception
        /// </summary>
        void HandleMalformedPacket(ICcpRemoteEndpoint remoteEndpoint)
        { 
            //todo
        }
        void HandleBadStatelessPowPacket(ICcpRemoteEndpoint remoteEndpoint)
        {
            //todo
        }
        void HandleBadSnonce0(ICcpRemoteEndpoint remoteEndpoint)
        {
        }
        #endregion

        #region hello0 
        void RespondToHello0(ICcpRemoteEndpoint clientEndpoint, ServerHello0Status status, byte[] cnonce0)
        {
            var response = new ServerHelloPacket0 { Status = status, Cnonce0 = cnonce0 };
            var responseBytes = response.Encode();
            _ccpTransport.SendPacket(clientEndpoint, responseBytes);
        }        
        void ProcessClientHello0(ICcpRemoteEndpoint clientEndpoint, BinaryReader reader, byte[] payloadData) // packets processor thread
        {
            var packet = new ClientHelloPacket0(reader, payloadData);
            if (!PassStatelessPoWfilter(clientEndpoint, packet))
                return;
                        
            // create snonce0 state
            var snonce0 = _snonce0Table.GenerateOrGetExistingSnonce0(TimeSec32UTC, clientEndpoint);
            
            var response = new ServerHelloPacket0
            {
                Cnonce0 = packet.Cnonce0,
                Snonce0 = snonce0.Snonce0,
                Status = ServerHello0Status.OK,
                StatefulProofOfWorkType = StatefulProofOfWorkType._2019_06
            };
            var responseBytes = response.Encode();
            _ccpTransport.SendPacket(clientEndpoint, responseBytes);
        }
        bool PassStatelessPoWfilter(ICcpRemoteEndpoint clientEndpoint, ClientHelloPacket0 packet)// packets processor thread // sends responses 
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
                            fixed (byte* addressBytesPtr = clientEndpoint.AddressBytes)
                            {
                                receivedTimeSec32 = *((uint*)statelessProofOfWorkDataPtr);                                
                                if (addressBytesPtr[0] != statelessProofOfWorkDataPtr[4] ||
                                    addressBytesPtr[1] != statelessProofOfWorkDataPtr[5] ||
                                    addressBytesPtr[2] != statelessProofOfWorkDataPtr[6] ||
                                    addressBytesPtr[3] != statelessProofOfWorkDataPtr[7]
                                    )
                                {
                                    if (_config.RespondErrors) RespondToHello0(clientEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadSourceIp, packet.Cnonce0);
                                    return false;
                                }
                             }
                        }
                    }


                    var localTimeSec32 = TimeSec32UTC;
                    var diffSec = Math.Abs((int)unchecked(localTimeSec32 - receivedTimeSec32));
                    if (diffSec > _config.StatelessPoW_MaxClockDifferenceS)
                    {
                        // respond with error "try again with valid clock" - legitimate user has to get valid clock from some time server and synchronize itself with the server
                        if (_config.RespondErrors) RespondToHello0(clientEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadClock, packet.Cnonce0);
                        return false;
                    }

                    var hash = _cryptoLibrary.GetHashSHA256(packet.OriginalPacketPayload);
                    // calculate hash, considering entire packet data (including stateless PoW result)
                    // verify hash result
                    if (!StatelessPowHashIsOK(hash))
                    {
                        HandleBadStatelessPowPacket(clientEndpoint);
                        // no response
                        return false;
                    }
                    
                    // check if hash is unique
                    var dataIsUnique = _recentUniquePowData.TryInputData(hash, localTimeSec32);                   

                    if (dataIsUnique)
                    {
                        return true;
                    }
                    else
                    {
                        // respond with error "try again with unique PoW data"
                        if (_config.RespondErrors) RespondToHello0(clientEndpoint, ServerHello0Status.ErrorTryAgainRightNowWithThisServer, packet.Cnonce0);
                        return false;
                    }
                default:
                    throw new CcpBadPacketException();
            }
        }
        internal static bool StatelessPowHashIsOK(byte[] hash)
        {
            if (hash[4] != 7 || (hash[5] != 7 && hash[5] != 8)
           //     || hash[6] > 100
                )
                return false;
            else return true;
            // devpc2: avg 30ms max 150ms
            // nova 2i: avg 200ms max 1257ms
            // honor 7      avg 363-475ms   max 1168-2130ms
        }
        #endregion

        #region hello1
        void ProcessClientHello1(ICcpRemoteEndpoint clientEndpoint, BinaryReader reader, byte[] payloadData) // packets processor thread
        {
            var snonce0 = _snonce0Table.TryGetSnonce0(clientEndpoint);
            if (snonce0 == null)
            {
                HandleBadSnonce0(clientEndpoint);
                return;
            }

            //TODO

            ///check stateful PoW result, snonce0
            ///
            /// questionable:    hello1IPlimit table:  limit number of requests  per 1 minute from every IPv4 block: max 100? requests per 1 minute from 1 block
            ///   ------------ possible attack on hello1IPlimit  table???
        }
        #endregion
    }

    public class CcpServerConfiguration
    {
        public int PacketProcessingQueueMaxCount = 100000; // 100k pps @1 sec queue time
        public int CcpUdpLocalServerPort = 9523;
        public int StatelessPoW_MaxClockDifferenceS = 20 * 60;
        public int StatelessPoW_RecentUniqueDataResetPeriodS = 10 * 60;
        /// <summary>
        /// turning this to "true" is not recommended in production mode, as it may create vulnerabilities
        /// </summary>
        public bool RespondErrors = false;
        public uint Snonce0TablePeriodSec = 5;
        public int Snonce0TableMaxSize = 500000;
        public int Snonce0Size = 32;
    }

    class Snonce0State
    {
        public byte[] Snonce0; // used to validate snonce received in ClientHello1
    }

    /// <summary>
    /// thread-unsafe
    /// generates snonce0 objects
    /// stores them for "period" = 5 seconds in Dictionary, by client endpoint
    /// max capacity: 100K per second, 5 seconds: 500K*snonce0 =     ...................
    /// </summary>
    class Snonce0Table
    {
        readonly Random _rnd = new Random();
        Dictionary<ICcpRemoteEndpoint, Snonce0State> _currentPeriodStates = new Dictionary<ICcpRemoteEndpoint, Snonce0State>();
        Dictionary<ICcpRemoteEndpoint, Snonce0State> _previousPeriodStates = new Dictionary<ICcpRemoteEndpoint, Snonce0State>();
        uint _nextPeriodSwitchTimeSec32UTC;

        readonly CcpServerConfiguration _config;
        public Snonce0Table(uint timeSec32UTC, CcpServerConfiguration config)
        {
            _config = config;
            _nextPeriodSwitchTimeSec32UTC = timeSec32UTC + config.Snonce0TablePeriodSec;
        }
        /// <summary>
        /// generates new snonce0 object
        /// resets state when necessary
        /// </summary>
        public Snonce0State GenerateOrGetExistingSnonce0(uint timeSec32UTC, ICcpRemoteEndpoint clientEndpoint)
        {
            if (timeSec32UTC > _nextPeriodSwitchTimeSec32UTC || _currentPeriodStates.Count > _config.Snonce0TableMaxSize)
            { // switch tables
                _previousPeriodStates = _currentPeriodStates;
                _currentPeriodStates = new Dictionary<ICcpRemoteEndpoint, Snonce0State>();
                _nextPeriodSwitchTimeSec32UTC = timeSec32UTC + _config.Snonce0TablePeriodSec;
            }

            var existingSnonce0 = TryGetSnonce0(clientEndpoint);
            if (existingSnonce0 != null) return existingSnonce0;

            var r = new Snonce0State
            {
                Snonce0 = new byte[_config.Snonce0Size]
            };
            _rnd.NextBytes(r.Snonce0);
            _currentPeriodStates.Add(clientEndpoint, r);
            return r;
        }
        public Snonce0State TryGetSnonce0(ICcpRemoteEndpoint clientEndpoint)
        {
            if (_currentPeriodStates.TryGetValue(clientEndpoint, out var r))
                return r;
            if (_previousPeriodStates.TryGetValue(clientEndpoint, out r))
                return r;
            return null;
        }
    }

    
    /// <summary>
    /// thread-unsafe
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
        readonly CcpServerConfiguration _config;
        public UniqueDataTracker(uint timeSec32UTC, CcpServerConfiguration config)
        {
            _config = config;
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
            
            if (unchecked(timeSec32UTC - _latestResetTimeSec32UTC) > _config.StatelessPoW_RecentUniqueDataResetPeriodS)
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
