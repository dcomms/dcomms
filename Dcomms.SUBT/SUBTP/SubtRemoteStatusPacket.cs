using Dcomms.P2PTP;
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.SUBT.SUBTP
{
    internal class SubtRemoteStatusPacket
    {
        public readonly float RecentRxBandwidth; // per stream
        public float RecentRxPacketLoss => _recentRxPacketLoss; // per stream
        public readonly float _recentRxPacketLoss; 
        public readonly float RecentTxBandwidth; // per connected peer (all connected streams between peers)
        public readonly bool IhavePassiveRole; // no own-set TX bandwidth target

        static float LimitPacketLoss(float loss)
        {
            if (loss > 1) loss = 1;
            else if (loss < 0) loss = 0;
            return loss;
        }
        public SubtRemoteStatusPacket(float recentRxBandwidth, float recentRxPacketLoss, float recentTxBandwidth, bool ihavePassiveRole)
        {
            RecentRxBandwidth = recentRxBandwidth;
            _recentRxPacketLoss = LimitPacketLoss(recentRxPacketLoss);
            RecentTxBandwidth = recentTxBandwidth;
            IhavePassiveRole = ihavePassiveRole;
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
         //   IwantToIncreaseBandwidthUntilHighPacketLoss = (flags & 0x01) != 0;
            IhavePassiveRole = (flags & 0x02) != 0;
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
              //  if (IwantToIncreaseBandwidthUntilHighPacketLoss) flags |= 0x01;
                if (IhavePassiveRole) flags |= 0x02;

                writer.Write(flags);
            }
            return ms.ToArray();
        }
    }


}
