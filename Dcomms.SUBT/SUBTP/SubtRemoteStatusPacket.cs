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
        public float RecentRxPacketLoss => _recentRxPacketLoss; // per stream // 0..1
        public readonly float _recentRxPacketLoss;  // 0..1
        public readonly float RecentTxBandwidth; // actual transmitted bandwidth // per connected peer (all connected streams between peers)
        public readonly bool IhavePassiveRole; // no own-set TX bandwidth target
        public readonly bool IwantToIncreaseBandwidth;
        public readonly bool IwantToDecreaseBandwidth;

        static float LimitPacketLoss(float loss)
        {
            if (loss > 1) loss = 1;
            else if (loss < 0) loss = 0;
            return loss;
        }
        public SubtRemoteStatusPacket(float recentRxBandwidth, float recentRxPacketLoss, float recentTxBandwidth, bool ihavePassiveRole, bool iwantToIncreaseBandwidth, bool iwantToDecreaseBandwidth)
        {
            RecentRxBandwidth = recentRxBandwidth;
            _recentRxPacketLoss = LimitPacketLoss(recentRxPacketLoss);
            RecentTxBandwidth = recentTxBandwidth;
            IhavePassiveRole = ihavePassiveRole;
            IwantToIncreaseBandwidth = iwantToIncreaseBandwidth;
            IwantToDecreaseBandwidth = iwantToDecreaseBandwidth;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("rxBwS={0};rxLossS={1:0.0}%;txBwP={2};passive={3}", RecentRxBandwidth.BandwidthToString(), RecentRxPacketLoss * 100, RecentTxBandwidth.BandwidthToString(), IhavePassiveRole);          
            return sb.ToString();
        }

        public SubtRemoteStatusPacket(BinaryReader reader)
        {
            RecentRxBandwidth = reader.ReadSingle();
            _recentRxPacketLoss = LimitPacketLoss(reader.ReadSingle());
            RecentTxBandwidth = reader.ReadSingle();
            var flags = reader.ReadByte();
            IhavePassiveRole = (flags & 0x02) != 0;
            IwantToIncreaseBandwidth = (flags & 0x04) != 0;
            IwantToDecreaseBandwidth = (flags & 0x08) != 0;

        }
        public byte[] Encode(SubtConnectedPeerStream connectedStream)
        {
            P2ptpCommon.CreateBinaryWriter(out var ms, out var writer);           
            
            ExtensionProcedures.InitializeExtensionSignalingPacket(writer, connectedStream.SubtLocalPeer.LocalPeer.LocalPeerId, connectedStream.SubtConnectedPeer.RemotePeerId, connectedStream.StreamId, connectedStream.SubtLocalPeer.ExtensionId);
            writer.Write((byte)SubtPacketType.RemoteStatus);
            writer.Write(RecentRxBandwidth);
            writer.Write(RecentRxPacketLoss);
            writer.Write(RecentTxBandwidth);
            byte flags = 0;
            if (IhavePassiveRole) flags |= 0x02;
            if (IwantToIncreaseBandwidth) flags |= 0x04;
            if (IwantToDecreaseBandwidth) flags |= 0x08;
            writer.Write(flags);
            
            return ms.ToArray();
        }
    }


}
