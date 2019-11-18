using Dcomms.P2PTP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.SUBT.SUBTP
{
    internal class AdjustmentResponsePacket
    {
        public readonly float TxTargetBandwidth;

        public AdjustmentResponsePacket(float txTargetBandwidth)
        {
            TxTargetBandwidth = txTargetBandwidth;
        }
        public override string ToString() => $"txTargetBandwidth={TxTargetBandwidth}";

        public AdjustmentResponsePacket(BinaryReader reader)
        {
            var flags = reader.ReadByte(); // not used now
            TxTargetBandwidth = reader.ReadSingle();
        }
        public byte[] Encode(SubtConnectedPeerStream connectedStream)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            ExtensionProcedures.InitializeExtensionSignalingPacket(writer, connectedStream.SubtLocalPeer.LocalPeer.LocalPeerId, connectedStream.SubtConnectedPeer.RemotePeerId, connectedStream.StreamId, connectedStream.SubtLocalPeer.ExtensionId);
            writer.Write((byte)SubtPacketType.AdjustmentResponse);

            byte flags = 0; // not used now           
            writer.Write(flags);
            writer.Write(TxTargetBandwidth);

            return ms.ToArray();
        }
    }
}
