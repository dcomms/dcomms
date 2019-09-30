using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    public class DrpPeerEngineConfiguration
    {
        public int? InsecureRandomSeed;
        public ushort? LocalPort;
        /// <summary>
        /// is used instead of public IP API provider response; in case of localhost-localhost tests 
        /// </summary>
        public IPAddress ForcedPublicIpApiProviderResponse;
        public TimeSpan PingRequestsInterval = TimeSpan.FromSeconds(2);
        public double PingRetransmissionInterval_RttRatio = 2.0; // "how much time to wait until sending another ping request?" - coefficient, relative to previously measured RTT
        public TimeSpan ConnectedPeersRemovalTimeout => PingRequestsInterval + TimeSpan.FromSeconds(2);

        public uint RegisterPow1_RecentUniqueDataResetPeriodS = 10 * 60;
        public int RegisterPow1_MaxTimeDifferenceS = 20 * 60;
        public bool RespondToRegisterPow1Errors = false;

        public TimeSpan Pow2RequestStatesTablePeriod = TimeSpan.FromSeconds(5);
        public int Pow2RequestStatesTableMaxSize = 100000;
        /// <summary>
        /// max allowed difference between local system clock and received REQ timestamps at responder or proxy
        /// </summary>
        public int MaxReqTimestampDifferenceS = 20 * 60;

        public TimeSpan PendingRegisterRequestsTimeout = TimeSpan.FromSeconds(20);

        public double UdpLowLevelRequests_ExpirationTimeoutS = 5;
        public double UdpLowLevelRequests_InitialRetransmissionTimeoutS = 0.35;
        public double UdpLowLevelRequests_RetransmissionTimeoutIncrement = 1.5;
        public double RegisterRequestsTimoutS = 15;

        public double InitialPingRequests_ExpirationTimeoutS = 5;
        public double InitialPingRequests_InitialRetransmissionTimeoutS = 0.2;
        public double InitialPingRequests_RetransmissionTimeoutIncrement = 1.05;
        public TimeSpan ResponderToRetransmittedRequestsTimeout = TimeSpan.FromSeconds(30);

        public double InviteRequestsTimoutS = 15;

        public VisionChannel VisionChannel;
        public string VisionChannelSourceId;

        public bool SandboxModeOnly_DisableRecentUniquePow1Data; //avoids huge RAM consumption when 100 peers are created in a sandbox

        public double NeighborhoodExtensionMaxRetryIntervalS = 20;
        public double NeighborhoodExtensionMinIntervalS = 3;
    }
}
