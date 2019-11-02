using Dcomms.P2PTP.Extensibility;
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dcomms.P2PTP
{
    public static class ExtensionProcedures
    {
        public static int InitializePayloadPacketForExtension(byte[] extensionHeader, byte[] data, StreamId streamId)
        {
            Array.Copy(extensionHeader, data, extensionHeader.Length);
            var index = PacketHeaders.SubtPayload.Length;
            StreamId.Encode(streamId, data, ref index);
            return index;
        }
        static bool ExtensionPacketHeaderMatches(ILocalPeerExtension extension, byte[] data)
        {//todo unsafe
            var h = extension.PayloadPacketHeader;
            if (h != null)
            {
                if (data.Length >= h.Length)
                {
                    for (int i = 0; i < h.Length; i++)
                        if (data[i] != h[i])
                            return false;
                    return true;
                }
            }
            return false;
        }
       
        internal static (ILocalPeerExtension extension, StreamId streamId, int index) ParseReceivedExtensionPayloadPacket(byte[] data, ILocalPeerExtension[] extensions)
        {
            var extension = extensions.FirstOrDefault(x => ExtensionPacketHeaderMatches(x, data));
            if (extension != null)
            {
                var index = extension.PayloadPacketHeader.Length;
                var streamId = StreamId.Decode(data, ref index);
                return (extension, streamId, index);
            }
            return (null, null, 0);
        }

        public const int SignalingPacketMinEncodedSize = P2ptpCommon.HeaderSize + PeerId.EncodedSize + StreamId.EncodedSize;
        internal static (PeerId fromPeerId, PeerId toPeerId, StreamId streamId, string extensionId) ParseExtensionSignalingPacket(BinaryReader reader)
        {
            var fromPeerId = PeerId.Decode(reader);
            var toPeerId = PeerId.Decode(reader);
            var streamId = StreamId.Decode(reader);
            var extensionId = PacketProcedures.DecodeString1ASCII(reader);
            return (fromPeerId, toPeerId, streamId, extensionId);
        }
        public static void InitializeExtensionSignalingPacket(BinaryWriter writer, PeerId fromPeerId, PeerId toPeerId, StreamId streamId, string extensionId)
        {
            if (fromPeerId == null) throw new ArgumentNullException(nameof(fromPeerId));
            if (toPeerId == null) throw new ArgumentNullException(nameof(toPeerId));
            if (streamId == null) throw new ArgumentNullException(nameof(streamId));
            P2ptpCommon.EncodeHeader(writer, PacketTypes.extensionSignaling);
            PeerId.Encode(writer, fromPeerId);
            PeerId.Encode(writer, toPeerId);
            StreamId.Encode(writer, streamId);
            PacketProcedures.EncodeString1ASCII(writer, extensionId);
        }
    }
}
