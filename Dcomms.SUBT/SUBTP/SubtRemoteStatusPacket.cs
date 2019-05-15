using Dcomms.P2PTP;
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.SUBT.SUBTP
{
    /// <summary>
    /// information about ConnectedPeerStream state, regularly shared between peers
    /// </summary>
    internal class SubtRemoteStatusPacket
    {
        public readonly float RecentRxBandwidth; // per stream
        public float RecentRxPacketLoss => _recentRxPacketLoss; // per stream
        public readonly float _recentRxPacketLoss; 
        public readonly float RecentTxBandwidth; // actual transmitted bandwidth // per connected peer (all connected streams between peers)
        public readonly bool IhavePassiveRole; // no own-set TX bandwidth target
                                               //   public readonly bool IwantToActivateThisStream; // stream becomes active when both parties confirm the activation
        public readonly float TargetTxBandwidth0; // stage #0 // per stream // TargetTxBandwidth0=0 means inactive stream
        public readonly float TargetTxBandwidth; // per stream 
        public readonly uint RequestId; // for future use, for RTT measurements
        public readonly uint HealthStatus1; // for future use, to avoid unhealthy peers
        public readonly uint HealthStatus2; // for future use, to avoid unhealthy peers

        static float LimitPacketLoss(float loss)
        {
            if (loss > 1) loss = 1;
            else if (loss < 0) loss = 0;
            return loss;
        }
        public SubtRemoteStatusPacket(float recentRxBandwidth, float recentRxPacketLoss, float recentTxBandwidth, bool ihavePassiveRole, float targetTxBandwidth0, float targetTxBandwidth)
        {
            RecentRxBandwidth = recentRxBandwidth;
            _recentRxPacketLoss = LimitPacketLoss(recentRxPacketLoss);
            RecentTxBandwidth = recentTxBandwidth;
            IhavePassiveRole = ihavePassiveRole;
            TargetTxBandwidth0 = targetTxBandwidth0;
            TargetTxBandwidth = targetTxBandwidth;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("rxBwS={0};rxLossS={1:0.0}%;txBwP={2}", RecentRxBandwidth.BandwidthToString(), RecentRxPacketLoss * 100, RecentTxBandwidth.BandwidthToString());          
            return sb.ToString();
        }

        public SubtRemoteStatusPacket(BinaryReader reader)
        {
            RecentRxBandwidth = reader.ReadSingle();
            _recentRxPacketLoss = LimitPacketLoss(reader.ReadSingle());
            RecentTxBandwidth = reader.ReadSingle();
            var flags = reader.ReadByte();
            IhavePassiveRole = (flags & 0x02) != 0;
            var version190515 = (flags & 0x04) != 0; // version190515: new fields StatelessTargetTxBandwidth, TargetTxBandwidth, RequestId, HealthStatus1, HealthStatus2
            if (version190515)
            {
                TargetTxBandwidth = reader.ReadSingle();
                RequestId = reader.ReadUInt32();
                HealthStatus1 = reader.ReadUInt32();
                HealthStatus2 = reader.ReadUInt32();
            }
        }
        public byte[] Encode(SubtConnectedPeerStream connectedStream)
        {
            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                ExtensionProcedures.InitializeExtensionSignalingPacket(writer, connectedStream.SubtLocalPeer.LocalPeer.LocalPeerId, connectedStream.SubtConnectedPeer.RemotePeerId, connectedStream.StreamId, connectedStream.SubtLocalPeer.ExtensionId);
                writer.Write((byte)SubtPacketType.RemoteStatus);
                writer.Write(RecentRxBandwidth);
                writer.Write(RecentRxPacketLoss);
                writer.Write(RecentTxBandwidth);
                byte flags = 0;
                if (IhavePassiveRole) flags |= 0x02;
                flags |= 0x04; // version190515: new fields StatelessTargetTxBandwidth, TargetTxBandwidth, RequestId, HealthStatus1, HealthStatus2

                writer.Write(flags);
                writer.Write(TargetTxBandwidth);
                writer.Write(RequestId);
                writer.Write(HealthStatus1);
                writer.Write(HealthStatus2);                
            }
            return ms.ToArray();
        }
    }


}
