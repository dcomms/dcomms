using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.CCP
{
    public class CcpClient: ICcpTransportUser, IDisposable
    {
        static ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        TimeSpan TimeSWE => _stopwatch.Elapsed; // stopwatch elapsed
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + TimeSWE; } }
        uint TimeSec32UTC => MiscProcedures.DateTimeToUint32seconds(DateTimeNowUtc);
        byte[] _localPublicIp;
        readonly CcpClientConfiguration _config;
        ICcpTransport _ccpTransport;
        Thread _ccpClientThread;

        #region state
        enum CcpClientState
        {
            None, // after ctor()
            RequestingLocalPublicIp,
            CreatingCcpTransport,
            ResolvingServerEndpoint,
            PreparingHello0,
            SentHello0, // retransmissions or receiving hello0 response from server
            ReceivedHello0Response,
            PreparingHello1,
            SentHello1, // retransmissions or receiving hello1 response from server

            Operation, // sending pings, receiving xx

            NonFatalError, // goes to "RequestingLocalPublicIp" after some time 
            FatalError
        }
        CcpClientState _state = CcpClientState.None;
        CcpClientState State
        {
            get => _state;
            set { _state = value; _stateLastStateTransitionTimeSWE = TimeSWE; }
        }
        TimeSpan _stateLastStateTransitionTimeSWE = TimeSpan.Zero;
        TimeSpan TimeSinceLastStateTransition => TimeSWE - _stateLastStateTransitionTimeSWE;
        CcpUdpRemoteEndpoint _currentServerEP;
        byte[] _hello0RequestPacketData;
        byte[] _cnonce0;
        int _transmittedRequestPacketsCount;
        bool _disposing;
        #endregion

        public CcpClient(CcpClientConfiguration config)
        {
            _config = config;

            _ccpClientThread = new Thread(CcpClientThreadEntry);
            _ccpClientThread.Name = "CCP client thread";
            _ccpClientThread.Start();

            BeginInitialize();
        }
        public void Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;
            _ccpClientThread.Join();
            _ccpTransport.Dispose();
        }
        #region before hello0
        /// <summary>
        /// state: RequestingLocalPublicIp   => BeginSendHello0()
        /// </summary>
        async void BeginInitialize()
        {
            try
            {
                State = CcpClientState.RequestingLocalPublicIp;
                _localPublicIp = await SendPublicApiRequestAsync("http://ip.seeip.org/");
                if (_localPublicIp == null) _localPublicIp = await SendPublicApiRequestAsync("http://api.ipify.org/");
                if (_localPublicIp == null) _localPublicIp = await SendPublicApiRequestAsync("http://bot.whatismyipaddress.com");

                if (_localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");

                CreateCcpTransport();
                BeginSendHello0();
            }
            catch (Exception exc)
            {
                HandleException(exc, "error when initializing");
                State = CcpClientState.NonFatalError;
            }
        }
        /// <returns>bytes of IP address</returns>
        async Task<byte[]> SendPublicApiRequestAsync(string url)
        {
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                var response = await httpClient.GetAsync(url);
                var result = await response.Content.ReadAsStringAsync();
                var ipAddress = IPAddress.Parse(result);
                return ipAddress.GetAddressBytes();
            }
            catch (Exception exc)
            {
                HandleException(exc, $"public api request to {url} failed");
                return null;
            }
        }
        void CreateCcpTransport()
        {
            State = CcpClientState.CreatingCcpTransport;
            if (_ccpTransport != null)
            {
                _ccpTransport.Dispose();
                _ccpTransport = null;
            }
            _ccpTransport = new CcpUdpTransport(this);
        }
        #endregion
        #region hello0
        void BeginSendHello0()
        {
            State = CcpClientState.ResolvingServerEndpoint;
            var serverUrl = _config.ServerUrls[0];
            _currentServerEP = new CcpUdpRemoteEndpoint(serverUrl);
                
            // generate new client session token
            _cnonce0 = new byte[ClientHelloPacket0.Cnonce0SupportedSize];
            using (var g = new RNGCryptoServiceProvider())
                g.GetBytes(_cnonce0);

            State = CcpClientState.PreparingHello0;
            _hello0RequestPacketData = GenerateNewClientHelloPacket0(_localPublicIp, TimeSec32UTC, _cnonce0);
            _transmittedRequestPacketsCount = 0;

            // send hello0
            State = CcpClientState.SentHello0;
            SendHello0();
        }
        void SendHello0()
        {
            _transmittedRequestPacketsCount++;
            _ccpTransport.SendPacket(_currentServerEP, _hello0RequestPacketData);

        }

        /// <summary>
        /// performs stateless proof of work
        /// </summary>
        public static byte[] GenerateNewClientHelloPacket0(byte[] clientPublicIp, uint timeSec32UTC, byte[] cnonce0)
        {
            var r = new ClientHelloPacket0();
            r.Cnonce0 = cnonce0;

            r.StatelessProofOfWorkType = StatelessProofOfWorkType._2019_06;
            r.StatelessProofOfWorkData = new byte[32];
            using (var writerPoW = new BinaryWriter(new MemoryStream(r.StatelessProofOfWorkData)))
            {
                writerPoW.Write(timeSec32UTC);
                writerPoW.Write(clientPublicIp);
            }

            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            var powRandomDataPosition = 8 + r.Encode(writer);
            var packetData = ms.ToArray();
            var rnd = new Random();
            var rndData = new byte[packetData.Length - powRandomDataPosition];
            for (; ;)
            {
                var hash = _cryptoLibrary.GetHashSHA256(packetData);
                if (CcpServer.StatelessPowHashIsOK(hash)) break;
                rnd.NextBytes(rndData);
                Buffer.BlockCopy(rndData, 0, packetData, powRandomDataPosition, rndData.Length);
            }
            
            return packetData;
        }

        void CcpClientThread_SentHello0()
        {
            switch (_transmittedRequestPacketsCount)
            {
                case 1:
                    if (TimeSinceLastStateTransition > _config.RetransmissionT1) SendHello0();
                    break;
                case 2:
                    if (TimeSinceLastStateTransition > _config.RetransmissionT2) SendHello0();
                    break;
                case 3:
                    if (TimeSinceLastStateTransition > _config.RetransmissionT3) SendHello0();
                    break;
                case 4:
                    if (TimeSinceLastStateTransition > _config.RetransmissionT4) SendHello0();
                    break;
                case 5:
                    if (TimeSinceLastStateTransition > _config.RetransmissionT5) SendHello0();
                    break;
                default:
                    HandleException(new Exception($"hello0 request timeout to server {_currentServerEP}"), "can not conenct to server");
                    State = CcpClientState.NonFatalError; // todo switch to another server URL
                    break;
            }
        }
        void ProcessPacket_SentHello0(ICcpRemoteEndpoint remoteEndpoint, byte[] udpData) //receiver thread
        {
            if (udpData[0] != (byte)CcpPacketType.ServerHelloPacket0)
            {
                OnReceivedBadPacket(remoteEndpoint, "invalid packet type 3457"); // unexpected packet
                return;
            }

            var responsePacket = new ServerHelloPacket0(udpData);
            
            // validate  responsePacket.ClientHelloToken
            if (!MiscProcedures.EqualByteArrays(_cnonce0, responsePacket.Cnonce0))
            { 
                OnReceivedBadPacket(remoteEndpoint, "invalid client hello token 3493");
                return;
            }

            State = CcpClientState.ReceivedHello0Response;
            switch (responsePacket.Status)
            {
                case ServerHello0Status.OK:
                    switch (responsePacket.StatefulProofOfWorkType)
                    {
                        case StatefulProofOfWorkType._2019_06:
                            State = CcpClientState.PreparingHello1;

                            //todo
                           // responsePacket.Snonce0
                    
                                // send hello1
                                // retransmit if no response N times

                                // handle response

                                // send pings
                                // if failed - keep reconnecting  after N secs
                            break;
                        default:
                            HandleException(new Exception($"server {_currentServerEP} responded with unknown PoW type {responsePacket.StatefulProofOfWorkType}"), "server rejected connection");
                            State = CcpClientState.FatalError;
                            break;
                    }

                    break;
                case ServerHello0Status.ErrorTryLaterWithThisServer:
                    State = CcpClientState.NonFatalError;
                    break;
                case ServerHello0Status.ErrorTryAgainRightNowWithThisServer:
                    BeginInitialize();
                    break;
                default: // got error response from server // we can not trust it, it can be fake response from MITM (questionable)
                    HandleException(new Exception($"server {_currentServerEP} responded with {responsePacket.Status}"), "server rejected connection");
                    State = CcpClientState.NonFatalError;
                    break;
            }
        }
        #endregion

        void CcpClientThreadEntry()
        {
            while (!_disposing)
            {
                try
                {
                    switch (State)
                    {
                        case CcpClientState.NonFatalError:
                            if (TimeSinceLastStateTransition > _config.NonFatalErrorReinitializationTimeout)
                                BeginInitialize();
                            break;
                        case CcpClientState.SentHello0:
                            CcpClientThread_SentHello0();
                            break;
                    }
                    Thread.Sleep(100);
                }               
                catch (Exception exc)
                {
                    HandleException(exc, "exception in CCP thread");
                }
            }
        }

        //async Task<byte[]> SendRequestAsync(byte[] requestPacket, byte expectedFirstByteInResponse)
        //{
        //    // filter only packets from server 
        //    throw new NotImplementedException();
        //}
               
        void HandleException(Exception exc, string description)
        {
//todo report to log/dev vision
        }

        #region process received packets
        void ICcpTransportUser.ProcessPacket(ICcpRemoteEndpoint remoteEndpoint, byte[] data)
        {
            if (_currentServerEP != null && _currentServerEP.Equals(remoteEndpoint))
            {
                switch (State)
                {
                    case CcpClientState.SentHello0:
                        ProcessPacket_SentHello0(remoteEndpoint, data);
                        break;
                }
            }
            else 
                OnReceivedPacketsFromBadSource(remoteEndpoint);
        }
        void OnReceivedPacketsFromBadSource(ICcpRemoteEndpoint remoteEndpoint)
        {/// todo report attack
        }
        void OnReceivedBadPacket(ICcpRemoteEndpoint remoteEndpoint, string errorDescription)
        {/// todo report attack
        }
        void ICcpTransportUser.HandleExceptionInCcpReceiverThread(Exception exc)
        {
            HandleException(exc, "exception in CCP receiver thread");
        }
        #endregion
    }
    public class CcpClientConfiguration
    {
        public CcpUrl[] ServerUrls { get; set; }
        static CcpClientConfiguration Default => new CcpClientConfiguration
        {
            ServerUrls = new[] { new CcpUrl("ccp://localhost:9523") }
        };

        public TimeSpan NonFatalErrorReinitializationTimeout = TimeSpan.FromSeconds(5);
        public TimeSpan RetransmissionT1 = TimeSpan.FromSeconds(0.5);
        public TimeSpan RetransmissionT2 = TimeSpan.FromSeconds(1);
        public TimeSpan RetransmissionT3 = TimeSpan.FromSeconds(2);
        public TimeSpan RetransmissionT4 = TimeSpan.FromSeconds(4);
        public TimeSpan RetransmissionT5 = TimeSpan.FromSeconds(8);
    }
}
