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
        uint TimeSec32UTC => MiscProcedures.DateTimeToUint32seconds(DateTimeNowUtc);
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
        readonly UniqueDataFilter16MbRAM _recentUniquePowData; // accessed by processor thread only
        readonly Snonce0Table _snonce0Table; // accessed by processor thread only
        #endregion
        public CcpServer(CcpServerConfiguration config)
        {
            _config = config;
            _recentUniquePowData = new UniqueDataFilter16MbRAM(TimeSec32UTC, _config.StatelessPoW_RecentUniqueDataResetPeriodS);
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
        {//todo
        }
        void HandleBadStatefulPowPacket(ICcpRemoteEndpoint remoteEndpoint)
        {
            //todo
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
            
            var packet = new ClientHelloPacket1(reader, payloadData);

            // check snonce0
            if (!MiscProcedures.EqualByteArrays(packet.Snonce0, snonce0.Snonce0))
            {
                HandleBadSnonce0(clientEndpoint);
                return;
            }

            ///check stateful PoW result
            var hash = _cryptoLibrary.GetHashSHA256(packet.OriginalPacketPayload);
            // calculate hash, considering entire packet data (including stateful PoW result)
            // verify hash result
            if (!StatefulPowHashIsOK(hash))
            {
                HandleBadStatefulPowPacket(clientEndpoint);
                // no response
                return;
            }

            // questionable:    hello1IPlimit table:  limit number of requests  per 1 minute from every IPv4 block: max 100? requests per 1 minute from 1 block
            //   ------------ possible attack on hello1IPlimit  table???
               

            var response = new ServerHelloPacket1 { Status = ServerHello1Status.OKready, Cnonce1 = packet.StatefulProofOfWorkResponseData };
            var responseBytes = response.Encode();
            _ccpTransport.SendPacket(clientEndpoint, responseBytes);

        }

        internal static bool StatefulPowHashIsOK(byte[] hash)
        {
            if (hash[4] != 8 || (hash[5] != 9 && hash[5] != 10)
                //     || hash[6] > 100
                )
                return false;
            else return true;
        }
        #endregion
    }

    public class CcpServerConfiguration
    {
        public int PacketProcessingQueueMaxCount = 100000; // 100k pps @1 sec queue time
        public int CcpUdpLocalServerPort = 9523;
        public int StatelessPoW_MaxClockDifferenceS = 20 * 60;
        public uint StatelessPoW_RecentUniqueDataResetPeriodS = 10 * 60;
        /// <summary>
        /// turning this to "true" is not recommended in production mode, as it may create vulnerabilities
        /// </summary>
        public bool RespondErrors = false;
        public uint Snonce0TablePeriodSec = 5;
        public int Snonce0TableMaxSize = 500000;
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
                Snonce0 = new byte[ServerHelloPacket0.Snonce0SupportedSize]
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

    
    
    class CcpBadPacketException: Exception
    {
    }

    class CcpServerSideSession
    {
        uint LatestActivityTime32S { get; set; } // to remove it on timeout
      //  byte[] ServerSessionToken;
     //   byte[] ClientHelloToken;

        IPEndPoint ClientEndpoint { get; set; }
        
        StatefulProofOfWorkType PowType { get; set; }
        byte[] PoWrequestData { get; set; } // pow for ping request, against stateful DoS attacks
    }

    
}
