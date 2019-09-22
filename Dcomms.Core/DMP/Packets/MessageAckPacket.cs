using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DMP.Packets
{
    class MessageAckPacket
    {
        public uint MessageId32;
        public MessageSessionStatusCode ReceiverStatus;

        // flags
        const byte FlagsMask_MustBeZero = 0b11110000;

        public const int ReceiverFinalNonceSize = 8;
        public byte[] ReceiverFinalNonce; // 8 bytes // is nut null when receiverStatus=encryptionDecryptionCompleted
        public HMAC MessageHMAC;


        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpDmpPacketTypes.MessageAck);
            writer.Write(MessageId32);
            writer.Write((byte)ReceiverStatus);
            byte flags = 0;
            writer.Write(flags);
            if (ReceiverStatus == MessageSessionStatusCode.encryptionDecryptionCompleted)
                writer.Write(ReceiverFinalNonce);

            MessageHMAC.Encode(writer);

            var r = ms.ToArray();
            if (r.Length > 500) throw new ArgumentException();
            return r;
        }
        public void GetSignedFieldsForMessageHMAC(BinaryWriter writer)
        {
            writer.Write(MessageId32);
            writer.Write((byte)ReceiverStatus);
            if (ReceiverFinalNonce != null) writer.Write(ReceiverFinalNonce);
        }
        public static MessageAckPacket Decode(byte[] udpData)
        {
            var r = new MessageAckPacket();
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);
            r.MessageId32 = reader.ReadUInt32();
            r.ReceiverStatus = (MessageSessionStatusCode)reader.ReadByte();

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();
            if (r.ReceiverStatus == MessageSessionStatusCode.encryptionDecryptionCompleted)
                r.ReceiverFinalNonce = reader.ReadBytes(ReceiverFinalNonceSize);

            r.MessageHMAC = HMAC.Decode(reader);
            return r;
        }

        internal static LowLevelUdpResponseScanner GetScanner(uint messageId32, InviteSession session, MessageSessionStatusCode statusCode)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.MessageAck);
            w.Write(messageId32);
            w.Write((byte)statusCode);

            return new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                OptionalFilter = (udpData) =>
                {
                    var msgAck = Decode(udpData);
                    if (msgAck.MessageHMAC.Equals(
                        session.GetMessageHMAC(msgAck.GetSignedFieldsForMessageHMAC)
                        ) == false)
                        return false;
                    return true;
                }
            };
        }
    }
}
