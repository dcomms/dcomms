using Dcomms.P2PTP.LocalLogic;
using Dcomms.SUBT;
using Dcomms;
using System;
using System.Net;

namespace StarTrinity.ContinuousSpeedTest.CLI
{
    class Program
    {
        class User : ILocalPeerUser
        {
            bool ILocalPeerUser.EnableLog => true;
            void ILocalPeerUser.WriteToLog(string message)
            {
             //   Console.WriteLine(message);
            }
        }
        static void Main(string[] args)
        {
            var coordinatorServerIp1 = IPAddress.Parse("163.172.210.13");//neth3
            var coordinatorServerIp2 = IPAddress.Parse("195.154.173.208");//fra2
            var subtLocalPeer = new SubtLocalPeer(new SubtLocalPeerConfiguration
            {
                SenderThreadsCount = 4,
                BandwidthTarget = 50000,
            });
            var node = new LocalPeer(new LocalPeerConfiguration
            {
                RoleAsUser = true,
                LocalPeerUser = new User(),
                LocalUdpPortRangeStart = null,
                SocketsCount = 4,
                Coordinators = new IPEndPoint[]
                {
                    new IPEndPoint(coordinatorServerIp1, 10000),
                    new IPEndPoint(coordinatorServerIp1, 10001),
                    new IPEndPoint(coordinatorServerIp1, 10002),
                    new IPEndPoint(coordinatorServerIp1, 10003),
                    new IPEndPoint(coordinatorServerIp1, 10004),
                    new IPEndPoint(coordinatorServerIp1, 10005),
                    new IPEndPoint(coordinatorServerIp1, 10006),
                    new IPEndPoint(coordinatorServerIp1, 10007),
                    new IPEndPoint(coordinatorServerIp1, 9000),
                    new IPEndPoint(coordinatorServerIp1, 9001),
                    new IPEndPoint(coordinatorServerIp1, 9002),
                    new IPEndPoint(coordinatorServerIp1, 9003),
                    new IPEndPoint(coordinatorServerIp2, 9000),
                    new IPEndPoint(coordinatorServerIp2, 9001),
                    new IPEndPoint(coordinatorServerIp2, 9002),
                    new IPEndPoint(coordinatorServerIp2, 9003),
                },
                Extensions = new[]
                {
                    subtLocalPeer
                }
            });
            subtLocalPeer.MeasurementsHistory.OnAddedNewMeasurement += MeasurementsHistory_OnAddedNewMeasurement;

            Console.WriteLine("running test...");
            Console.WriteLine($"target bandwidth: {subtLocalPeer.Configuration.BandwidthTarget.BandwidthToString()}");
            Console.ReadLine();
            node.Dispose();
        }

        private static void MeasurementsHistory_OnAddedNewMeasurement(SubtMeasurement measurement)
        {
            Console.WriteLine($"measurement: download={measurement.RxBandwidthString} (packet loss={measurement.RxPacketLossString}), upload={measurement.TxBandwidthString} (packet loss={measurement.TxPacketLossString})");
        }
    }
}
