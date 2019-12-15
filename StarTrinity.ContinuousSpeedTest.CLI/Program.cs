using Dcomms.P2PTP.LocalLogic;
using Dcomms.SUBT;
using Dcomms;
using System;
using System.Net;
using Dcomms.Vision;

namespace StarTrinity.ContinuousSpeedTest.CLI
{
    class Program
    {
        static DateTime VersionDateTimeUtc => new DateTime(2019, 12, 15); // todo get it somehow automatically in both windows and linux



        static void Main(string[] args)
        {
            Console.WriteLine("usage: StarTrinity.ContinuousSpeedTest.CLI target 1000000\r\n" +
                "where  1000000=1M is target bandwidth, in bits per second\r\n" +
                "any questions/problems/suggestions - email to support@startrinity.com");
            var bandwidthBps = 1000000;
            if (args[0] == "target") bandwidthBps = int.Parse(args[1]);


            MiscProcedures.Initialize(VersionDateTimeUtc);
            var coordinatorServerIp1 = IPAddress.Parse("163.172.210.13");//neth3
            var coordinatorServerIp2 = IPAddress.Parse("195.154.173.208");//fra2
            var subtLocalPeer = new SubtLocalPeer(new SubtLocalPeerConfiguration
            {
                SenderThreadsCount = 4,
                BandwidthTarget = bandwidthBps,
            });
            var visionChannel = new VisionChannel1() { ClearLog_MessagesCount = 1000 };
            visionChannel.SevereMessageEmitted += (msg) =>
            {
                Console.WriteLine(msg.Message);
            };

            var node = new LocalPeer(new LocalPeerConfiguration
            {
                RoleAsUser = true,
                VisionChannel = visionChannel,
                LocalUdpPortRangeStart = null,
                SocketsCount = 4,
                Coordinators = new IPEndPoint[]
                {
                    new IPEndPoint(coordinatorServerIp1, 10000),
                    new IPEndPoint(coordinatorServerIp1, 10001),
                    new IPEndPoint(coordinatorServerIp1, 10002),
                    //new IPEndPoint(coordinatorServerIp1, 10003),
                    //new IPEndPoint(coordinatorServerIp1, 10004),
                    //new IPEndPoint(coordinatorServerIp1, 10005),
                    //new IPEndPoint(coordinatorServerIp1, 10006),
                    //new IPEndPoint(coordinatorServerIp1, 10007),
                    //new IPEndPoint(coordinatorServerIp1, 9000),
                    //new IPEndPoint(coordinatorServerIp1, 9001),
                    //new IPEndPoint(coordinatorServerIp1, 9002),
                    //new IPEndPoint(coordinatorServerIp1, 9003),
                    //new IPEndPoint(coordinatorServerIp2, 9000),
                    //new IPEndPoint(coordinatorServerIp2, 9001),
                    //new IPEndPoint(coordinatorServerIp2, 9002),
                    //new IPEndPoint(coordinatorServerIp2, 9003),
                },
                Extensions = new[]
                {
                    subtLocalPeer
                }
            });
            subtLocalPeer.MeasurementsHistory.OnMeasured += MeasurementsHistory_OnAddedNewMeasurement;

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
