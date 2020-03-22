using Dcomms.DRP.Packets;
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
        public bool SingleUdpChannel { get; set; }
    }
    public class NatTester: IDisposable
    {
        bool _disposing;
        Thread _receiverThread;
        UdpClient _socket;
        Dictionary<IPEndPoint, Result> _results; // locked

        NatTest1RequestPacket _requestPacket;
        byte[] _requestPacketData;

        private NatTester(IPEndPoint[] remoteEndpoints)
        {
            if (remoteEndpoints.Length < 2) throw new ArgumentException();
            _results = remoteEndpoints.ToDictionary(x => x, x => new Result());
            _socket = new UdpClient(0);

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
        public void Dispose()
        {
            _disposing = true;
            _socket.Close();
            _socket.Dispose();
            _receiverThread.Join();
        }



        class Result
        {
            public NatTest1ResponsePacket Response;
        }

        public static async Task<NatTestResult> Test(IPEndPoint[] remoteEndpoints)
        {
            // todo special GUI
            using var tester = new NatTester(remoteEndpoints);
                     

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
                else if (responsesCount >= 2) break;
                else if (swStart.Elapsed.TotalSeconds > 5) throw new NatTestException($"NAT test failed: no response from remote endpoints in {swStart.Elapsed.TotalSeconds} seconds");

                await Task.Delay(10);
                if (swTransmitted.Elapsed.TotalMilliseconds > 500)
                    goto _retransmit;
            }

            var responses = tester._results.Values.Where(x => x.Response != null).Select(x => x.Response).ToList();
            if (responses.Count < 2) throw new NatTestException("not enough responses 23438");

            return new NatTestResult()
            { 
                SingleUdpChannel = responses.Select(x => x.RequesterEndpoint).Distinct().Count() == 1,
                LocalPublicIpAddress = responses[0].RequesterEndpoint.Address
             };

        }
    }
}
