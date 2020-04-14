using Dcomms.DRP.Packets;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms
{
    class NatTestException: Exception
    {
        public NatTestException(string msg): base(msg)
        {

        }
    }
    public class NatTestResult 
    {
        public IPAddress LocalPublicIpAddress { get; set; }
        public DRP.NatBehaviourModel NatBehaviour { get; set; }
    }
    public class NatTest: IDisposable
    {
        const int TimeoutMs = 5000;

        bool _disposing;
        Thread _receiverThread;
        UdpClient _socket;
        bool _ownSocket;
        Dictionary<IPEndPoint, Result> _results; // locked

        NatTest1RequestPacket _requestPacket;
        byte[] _requestPacketData;
        VisionChannel _visionChannelNullable;
        string _visionChannelSourceId;

        private NatTest(IPEndPoint[] remoteEndpoints, string visionChannelSourceId, VisionChannel visionChannelNullable, UdpClient customSocketNullable)
        {
            _visionChannelSourceId = visionChannelSourceId;
            _visionChannelNullable = visionChannelNullable;
            _results = remoteEndpoints.Distinct().ToDictionary(x => x, x => new Result());
            if (remoteEndpoints.Length < 2) throw new ArgumentException("not enough unique remote endpoints");

            _socket = customSocketNullable ?? new UdpClient(0);
            if (customSocketNullable == null) _ownSocket = true;
            _socket.Client.ReceiveTimeout = TimeoutMs;

            // generate token
            _requestPacket = new NatTest1RequestPacket
            {
                Token32 = (uint)new Random().Next()
            };
            _requestPacketData = _requestPacket.Encode();


            _receiverThread = new Thread(ReceiverThread);
            _receiverThread.Start();
        }
        void ReceiverThread()
        {
            var receivedFromEP = default(IPEndPoint);
            while (!_disposing)
            {
                try
                {
                    
                    var data = _socket.Receive(ref receivedFromEP);
                    if (data[0] == (byte)PacketTypes.NatTest1Response)
                        if (_results.TryGetValue(receivedFromEP, out var result))
                        {
                            var response = NatTest1ResponsePacket.Decode(data);
                            WriteToLog($"received response from {receivedFromEP}: {response.RequesterEndpoint}");
                            if (response.Token32 == _requestPacket.Token32)
                                result.Response = response;
                        }
                }
                catch (Exception exc)
                {
                    //xxx
                }
            }
        }
        const string VisionChannelModuleName = "NatTest";
        void WriteToLog(string msg)
        {
            if (_visionChannelNullable != null)
            {
                if (_visionChannelNullable.GetAttentionTo(_visionChannelSourceId, VisionChannelModuleName) <= AttentionLevel.detail)
                    _visionChannelNullable.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.detail, msg);
            }
        }
        public void Dispose()
        {
            _disposing = true;
            if (_ownSocket)
            {
                _socket.Close();
                _socket.Dispose();
            }
            _receiverThread.Join();
        }



        class Result
        {
            public NatTest1ResponsePacket Response;
        }

        public static async Task<NatTestResult> Test(IPEndPoint[] remoteEndpoints, string visionChannelSourceId, VisionChannel visionChannelNullable, int maxResponsesCount = 2, UdpClient customSocketNullable = null)
        {
            // todo special GUI
            using var tester = new NatTest(remoteEndpoints, visionChannelSourceId, visionChannelNullable, customSocketNullable);
                     

            var swStart = Stopwatch.StartNew();

_retransmit:
            // send request to all remote endpoints
            foreach (var kv in tester._results)
            {
                if (kv.Value.Response == null)
                    try
                    {
                        await tester._socket.SendAsync(tester._requestPacketData, tester._requestPacketData.Length, kv.Key);
                    }
                    catch (Exception exc)
                    {
                        //todo  
                        // ignoring now
                    }
            }
            var swTransmitted = Stopwatch.StartNew();

            for (; ; )
            {
                // got enough of responses?
                var responsesCount = tester._results.Count(x => x.Value.Response != null);
                if (responsesCount == remoteEndpoints.Length) break;
                else if (responsesCount >= maxResponsesCount) break;
                else if (swStart.Elapsed.TotalMilliseconds > TimeoutMs)
                {
                    if (responsesCount < 2) throw new NatTestException($"NAT test failed: no response from remote endpoints in {swStart.Elapsed.TotalSeconds} seconds");
                    else break;
                }

                await Task.Delay(10);
                if (swTransmitted.Elapsed.TotalMilliseconds > 500)
                    goto _retransmit;
            }

            var responses = tester._results.Values.Where(x => x.Response != null).Select(x => x.Response).ToList();
            if (responses.Count < 2) throw new NatTestException("not enough responses 23438");
           

            //var requestedIpsCount = remoteEndpoints.Select(x => x.Address).Distinct().Count();
            //tester.WriteToLog($"responded {respondedIps.Count}/{requestedIpsCount} IPs: {String.Join(';', respondedIps.Select(x => x.ToString()))}");

            return new NatTestResult()
            { 
                NatBehaviour = new DRP.NatBehaviourModel
                {
                    PortsMappingIsStatic_ShortTerm = responses.Select(x => x.RequesterEndpoint).Distinct().Count() == 1,
                },       
                LocalPublicIpAddress = responses[0].RequesterEndpoint.Address
            };
        }
    }

    public class NatTester
    {
        public string RemoteEndpointsString { get; set; } = "192.99.160.225:12000;192.99.160.225:12001\r\n195.154.173.208:12000;195.154.173.208:12001\r\n5.135.179.50:12000;5.135.179.50:12001";
        IPEndPoint[] RemoteEndpoints
        {
            get
            {
                return (from str in RemoteEndpointsString.Split(new char[] { ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        let pos = str.IndexOf(':')
                        where pos != -1
                        select new IPEndPoint(
                            IPAddress.Parse(str.Substring(0, pos)),
                            int.Parse(str.Substring(pos + 1))
                            )
                        ).ToArray();
            }
        }
        readonly VisionChannel _visionChannel;
        readonly string _visionChannelSourceId;
        public NatTester(VisionChannel visionChannel, string visionChannelSourceId)
        {
            _visionChannel = visionChannel;
            _visionChannelSourceId = visionChannelSourceId;
        }



        public System.Windows.Input.ICommand Test => new DelegateCommand(() =>
        {
            _ = NatTest.Test(RemoteEndpoints, _visionChannelSourceId, _visionChannel, 100000); 
        });
    }
}
