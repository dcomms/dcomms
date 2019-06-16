using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.CCP
{
    class CcpClient
    {
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + _stopwatch.Elapsed; } }
        uint TimeSec32UTC => MiscProcedures.DateTimeToUint32(DateTimeNowUtc);
        byte[] _localPublicIp;
        readonly CcpClientConfiguration _config;
        public CcpClient(CcpClientConfiguration config)
        {
            _config = config;
            InitializeAsync();
            // todo on error keep reconnecting  after N secs
        }


        async Task InitializeAsync()
        {
            _localPublicIp = await SendPublicApiRequestAsync("http://api.ipify.org/");
            if (_localPublicIp == null) _localPublicIp = await SendPublicApiRequestAsync("http://ip.seeip.org/");
            if (_localPublicIp == null) _localPublicIp = await SendPublicApiRequestAsync("http://bot.whatismyipaddress.com");
            if (_localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");

            // todo  open udp socket
            
            var hello0 = GenerateNewClientHelloPacket0();

            // todo send hello0
            // retransmit if no response N times

            // handle response

            // send hello1
            // retransmit if no response N times

            // handle response


        }
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
        /// performs stateless PoW
        /// </summary>
        ClientHelloPacket0 GenerateNewClientHelloPacket0()
        {
            // xxxx
            /*    generate packet, encode with zero pow data
                rnd pow data
                loop

                hashes....*/
            throw new NotImplementedException();
        }
    }
    class CcpClientConfiguration
    {
        public CcpUrl[] ServerUrls { get; set; }
        static CcpClientConfiguration Default => new CcpClientConfiguration
        {
            ServerUrls = new[] { new CcpUrl("ccp://localhost:9523") }
        };
    }
}
