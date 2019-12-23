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
        public readonly float TxTargetBandwidth;      
      
        public AdjustmentRequestPacket(float txTargetBandwidth)
        {
            TxTargetBandwidth = txTargetBandwidth;
        }
        public override string ToString() => $"txTargetBandwidth={TxTargetBandwidth}";

        public AdjustmentRequestPacket(BinaryReader reader)
        {
            var flags = reader.ReadByte(); // not used now
            TxTargetBandwidth = reader.ReadSingle();
        }
        public byte[] Encode(SubtConnectedPeerStream connectedStream)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);           
            
            ExtensionProcedures.InitializeExtensionSignalingPacket(writer, connectedStream.SubtLocalPeer.LocalPeer.LocalPeerId, connectedStream.SubtConnectedPeer.RemotePeerId, connectedStream.StreamId, connectedStream.SubtLocalPeer.ExtensionId);
            writer.Write((byte)SubtPacketType.AdjustmentRequest);

            byte flags = 0; // not used now           
            writer.Write(flags);
            writer.Write(TxTargetBandwidth);
            
            return ms.ToArray();
        }
    }


}
