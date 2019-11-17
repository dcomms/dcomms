using Dcomms.P2PTP;
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.SUBT.SUBTP
{
 
    internal class AdjustmentRequestPacket
    {
        public readonly float RequestedTxBandwidthAtRemotePeer;      
      
        public AdjustmentRequestPacket(float requestedTxBandwidthAtRemotePeers)
        {
            RequestedTxBandwidthAtRemotePeer = requestedTxBandwidthAtRemotePeers;
        }
        public override string ToString() => $"requestedTxBandwidthAtRemotePeer={RequestedTxBandwidthAtRemotePeer}";

        public AdjustmentRequestPacket(BinaryReader reader)
        {
            var flags = reader.ReadByte(); // not used now
            RequestedTxBandwidthAtRemotePeer = reader.ReadSingle();
        }
        public byte[] Encode(SubtConnectedPeerStream connectedStream)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);           
            
            ExtensionProcedures.InitializeExtensionSignalingPacket(writer, connectedStream.SubtLocalPeer.LocalPeer.LocalPeerId, connectedStream.SubtConnectedPeer.RemotePeerId, connectedStream.StreamId, connectedStream.SubtLocalPeer.ExtensionId);
            writer.Write((byte)SubtPacketType.AdjustmentRequest);

            byte flags = 0; // not used now           
            writer.Write(flags);
            writer.Write(RequestedTxBandwidthAtRemotePeer);
            
            return ms.ToArray();
        }
    }


}
