using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP
{
    internal class PeersListPacketIpv4
    {
        internal const int MaxSharedPeersCountPerPacket = (LocalLogicConfiguration.UdpMaxPacketSizeWithoutHeadersBytes - EncodedSizeMinimum) / PeersListPacket_SharedPeerIpv4.EncodedSize;

        public readonly PeerId FromPeerId;
        public readonly StreamId StreamId; // element of authentication // comes from Hello level
        public readonly PeersListPacket_SharedPeerIpv4[] SharedPeers;

        public PeersListPacketIpv4(PeersListPacket_SharedPeerIpv4[] sharedPeers, PeerId fromPeerId, StreamId streamId)
        {
            SharedPeers = sharedPeers ?? throw new ArgumentNullException(nameof(sharedPeers));
            if (SharedPeers.Length > MaxSharedPeersCountPerPacket) throw new ArgumentException("Too many peers to share in a single packet");
            FromPeerId = fromPeerId ?? throw new ArgumentNullException(nameof(fromPeerId));
            StreamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        }
        public PeersListPacketIpv4(byte[] data)
        {
            if (data.Length < EncodedSizeMinimum) throw new ArgumentException(nameof(data));
            var index = P2ptpCommon.HeaderSize;
            FromPeerId = PeerId.Decode(data, ref index);
            StreamId = StreamId.Decode(data, ref index);

            var count = data[index++];
            SharedPeers = new PeersListPacket_SharedPeerIpv4[count];
            for (byte i = 0; i < count; i++)
                SharedPeers[i] = PeersListPacket_SharedPeerIpv4.Decode(data, ref index);          
        }
        const int EncodedSizeMinimum = P2ptpCommon.HeaderSize + PeerId.EncodedSize + StreamId.EncodedSize + 1;
        public byte[] Encode()
        {
            var data = new byte[EncodedSizeMinimum + SharedPeers.Length * PeersListPacket_SharedPeerIpv4.EncodedSize];
            P2ptpCommon.EncodeHeader(data, PacketType.peersListIpv4);
            var index = P2ptpCommon.HeaderSize;
            PeerId.Encode(FromPeerId, data, ref index);
            StreamId.Encode(StreamId, data, ref index);

            data[index++] = (byte)SharedPeers.Length;
            foreach (var peer in SharedPeers)
                peer.Encode(data, ref index);

            return data;            
        }
    }
    internal class PeersListPacket_SharedPeerIpv4
    {
        public StreamId FromSocketAtStreamId;
        public PeerId ToPeerId; // of shared peer
        public IPEndPoint ToEndPoint;
        public PeersListPacket_SharedPeerIpv4(StreamId fromSocketAtStreamId, PeerId toPeerId, IPEndPoint toEndPoint)
        {
            FromSocketAtStreamId = fromSocketAtStreamId;
            ToPeerId = toPeerId;
            ToEndPoint = toEndPoint;
        }
        public override string ToString()
        {
            return $"{ToEndPoint}:{ToPeerId}";
        }
        internal const int EncodedSize = StreamId.EncodedSize + PeerId.EncodedSize + 4 + 2;
        internal static PeersListPacket_SharedPeerIpv4 Decode(byte[] data, ref int index)
        {
            return new PeersListPacket_SharedPeerIpv4(   
                StreamId.Decode(data, ref index),
                PeerId.Decode(data, ref index),
                new IPEndPoint(new IPAddress(P2ptpCommon.DecodeUInt32(data, ref index)), P2ptpCommon.DecodeUInt16(data, ref index))
            );
        }
        internal void Encode(byte[] data, ref int index)
        {
            StreamId.Encode(FromSocketAtStreamId, data, ref index);
            PeerId.Encode(ToPeerId, data, ref index);
            if (ToEndPoint.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new NotSupportedException("only IPv4 is supported");            
#pragma warning disable CS0618 // Type or member is obsolete
            P2ptpCommon.EncodeUInt32(data, ref index, (uint)ToEndPoint.Address.Address);
#pragma warning restore CS0618 // Type or member is obsolete
            P2ptpCommon.EncodeUInt16(data, ref index, (ushort)ToEndPoint.Port);
        }
    }
}
