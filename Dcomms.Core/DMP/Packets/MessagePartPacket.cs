using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DMP.Packets
{
    class MessagePartPacket
    {
        public uint MessageId32;
        public MessageSessionStatusCode SenderStatus;

        // flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public byte[] ContinuedEncryptedData; // is not null when SenderStatus = inProgress   // so now it is not implemented
        public UserCertificateSignature SenderSignature; // is not null when SenderStatus = encryptionDecryptionCompleted
        public HMAC MessageHMAC;

        public byte[] Encode()
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)PacketTypes.MessagePart);
            writer.Write(MessageId32);
            writer.Write((byte)SenderStatus);
            byte flags = 0;
            writer.Write(flags);

            if (SenderStatus == MessageSessionStatusCode.inProgress) BinaryProcedures.EncodeByteArray65536(writer, ContinuedEncryptedData);
            else if (SenderStatus == MessageSessionStatusCode.encryptionDecryptionCompleted) SenderSignature.Encode(writer);

            MessageHMAC.Encode(writer);

            var r = ms.ToArray();
            if (r.Length > 500) throw new ArgumentException();
            return r;
        }

        internal void GetSignedFieldsForMessageHMAC(BinaryWriter writer)
        {
            writer.Write(MessageId32);
            writer.Write((byte)SenderStatus);
            if (SenderStatus == MessageSessionStatusCode.inProgress) BinaryProcedures.EncodeByteArray65536(writer, ContinuedEncryptedData);
            else if (SenderStatus == MessageSessionStatusCode.encryptionDecryptionCompleted) SenderSignature.Encode(writer);
        }


        internal byte[] DecodedUdpData;
        public static MessagePartPacket Decode(byte[] udpData)
        {
            var r = new MessagePartPacket();
            r.DecodedUdpData = udpData;
            var reader = BinaryProcedures.CreateBinaryReader(udpData, 1);
            r.MessageId32 = reader.ReadUInt32();
            r.SenderStatus = (MessageSessionStatusCode)reader.ReadByte();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();
                       
            if (r.SenderStatus == MessageSessionStatusCode.inProgress) r.ContinuedEncryptedData = BinaryProcedures.DecodeByteArray65536(reader);
            else if (r.SenderStatus == MessageSessionStatusCode.encryptionDecryptionCompleted)
                r.SenderSignature = UserCertificateSignature.Decode(reader);
            
            r.MessageHMAC = HMAC.Decode(reader);
            return r;
        }

        internal static LowLevelUdpResponseScanner GetScanner(uint messageId32, InviteSession session, MessageSessionStatusCode statusCode)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)PacketTypes.MessagePart);
            w.Write(messageId32);
            w.Write((byte)statusCode);

            return new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                OptionalFilter = (udpData) =>
                {
                    var msgPart = Decode(udpData);
                    if (msgPart.MessageHMAC.Equals(
                        session.GetMessageHMAC(msgPart.GetSignedFieldsForMessageHMAC)
                        ) == false)
                        return false;
                    return true;
                }
            };
        }

    }
}
