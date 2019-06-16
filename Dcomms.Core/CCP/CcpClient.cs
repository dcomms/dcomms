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
using System.Threading.Tasks;

namespace Dcomms.CCP
{
    public class CcpClient
    {
        static ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + _stopwatch.Elapsed; } }
        uint TimeSec32UTC => MiscProcedures.DateTimeToUint32(DateTimeNowUtc);
        byte[] _localPublicIp;
        readonly CcpClientConfiguration _config;
        byte[] _clientHelloToken;
        UdpClient _udpClient;
        public CcpClient(CcpClientConfiguration config)
        {

            _config = config;
            InitializeAsync();
        }


        async Task InitializeAsync()
        {
            _localPublicIp = await SendPublicApiRequestAsync("http://api.ipify.org/");
            if (_localPublicIp == null) _localPublicIp = await SendPublicApiRequestAsync("http://ip.seeip.org/");
            if (_localPublicIp == null) _localPublicIp = await SendPublicApiRequestAsync("http://bot.whatismyipaddress.com");
            if (_localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");
            
            // open udp socket resolving domain name from URL
            var serverUrl = _config.ServerUrls[0];
            _udpClient = new UdpClient(serverUrl.Host, serverUrl.Port);

            // generate new client session token
            _clientHelloToken = new byte[ClientHelloPacket0.ClientHelloTokenSupportedSize];
            using (var g = new RNGCryptoServiceProvider())
                g.GetBytes(_clientHelloToken);
          
            var hello0PacketData = GenerateNewClientHelloPacket0(_localPublicIp, TimeSec32UTC, _clientHelloToken);

            // send hello0
            await _udpClient.SendAsync(hello0PacketData, hello0PacketData.Length);

            // retransmit if no response N times
            _udpClient.ReceiveAsync();

            // handle response

            // send hello1
            // retransmit if no response N times

            // handle response


            // send pings
            // if failed - keep reconnecting  after N secs

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
        void HandleException(Exception exc, string description)
        {
//todo
        }

        /// <summary>
        /// performs stateless proof of work
        /// </summary>
        public static byte[] GenerateNewClientHelloPacket0(byte[] clientPublicIp, uint timeSec32UTC, byte[] clientHelloToken)
        {
            var r = new ClientHelloPacket0();
            r.ClientHelloToken = clientHelloToken;

            r.StatelessProofOfWorkType = StatelessProofOfWorkType._2019_06;
            r.StatelessProofOfWorkData = new byte[32];
            using (var writerPoW = new BinaryWriter(new MemoryStream(r.StatelessProofOfWorkData)))
            {
                writerPoW.Write(timeSec32UTC);
                writerPoW.Write(clientPublicIp);
            }

            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
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

            // xxxx
            /*
             *   generate packet, encode with pow data    = publicIp+dateTime
                rnd pow data
                loop

                hashes....*/

            return packetData;
        }
    }
    public class CcpClientConfiguration
    {
        public CcpUrl[] ServerUrls { get; set; }
        static CcpClientConfiguration Default => new CcpClientConfiguration
        {
            ServerUrls = new[] { new CcpUrl("ccp://localhost:9523") }
        };
    }
}
