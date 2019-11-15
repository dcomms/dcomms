using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.P2PTP.LocalLogic
{
    public static class LocalLogicConfiguration
    {
        public const ushort IpAndUdpHeadersSizeBytes = 28; // 20 bytes for the IP header + 8 bytes for the UDP header
        public const ushort IpAndUdpHeadersSizeBits = 28 * 8;
        public const ushort UdpMaxPacketSizeWithHeadersBytes = 1450;//  576; // IPv4 mandates a path MTU of at least 576 bytes
        public const ushort UdpMaxPacketSizeWithoutHeadersBytes = UdpMaxPacketSizeWithHeadersBytes - IpAndUdpHeadersSizeBytes; // IPv4 mandates a path MTU of at least 576 bytes
        public const ushort UdpMaxPacketSizeWithHeadersBits = UdpMaxPacketSizeWithHeadersBytes * 8;

        internal static readonly TimeSpan SharePeerConnectionsPeriod = TimeSpan.FromSeconds(10); // todo: dont send too frequently; send to new connected peer immedialtely
   

        internal const uint SharePeerConnectionsCount = 10; // count of shared peers in peerList packet
        public static readonly TimeSpan SendHelloRequestPeriod = TimeSpan.FromSeconds(2); // todo: optimize: dont send too frequently; only 
        internal static readonly TimeSpan MaxPeerIdleTime_ToRemove = SendHelloRequestPeriod + TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan MaxPeerIdleTime_ToShare = SendHelloRequestPeriod + TimeSpan.FromSeconds(1);

        internal static readonly TimeSpan ReinitializationTimeout = SendHelloRequestPeriod + TimeSpan.FromSeconds(5); // for clients only


        internal const int CoordinatorPeer_MaxConnectedPeersToAccept = 10000;
        internal const int SharedPeer_MaxConnectedPeersToAccept = 10000;
        internal const int UserPeer_MaxConnectedPeersToAccept = 100;
        internal const int ConnectedPeerMaxStreamsCount = 30; // 10 + 20 for re-initialization

    }
}
