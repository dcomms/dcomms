using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DMP.Packets
{
    class MessageStartPacket
    {
        public DirectChannelToken32 DirectChannelToken32;
        public uint MessageId32;

        // flags
        const byte FlagsMask_MustBeZero = 0b11110000;

        public Int64 MessageTimestamp64;
        public byte[] EncryptedMessageData;

        public HMAC MessageHMAC;

        internal static LowLevelUdpResponseScanner GetScanner(DirectChannelToken32 localDirectChannelToken32, InviteSession session)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.MessageStart);
            localDirectChannelToken32.Encode(w);            
            return new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                OptionalFilter = (udpData) =>
                {
                    var msgStart = Decode(udpData);
                    if (msgStart.MessageHMAC.Equals(
                        session.GetMessageHMAC(w2 => msgStart.GetSignedFieldsForMessageHMAC(w2, true))
                        ) == false)
                        return false;
                    return true;
                }
            };          
        }

        internal byte[] DecodedUdpData;
        public static MessageStartPacket Decode(byte[] udpData)
        {
            var r = new MessageStartPacket();
            r.DecodedUdpData = udpData;
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);
            r.DirectChannelToken32 = DirectChannelToken32.Decode(reader);
            r.MessageId32 = reader.ReadUInt32();
            r.MessageTimestamp64 = reader.ReadInt64();
            
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();
            
            r.EncryptedMessageData = PacketProcedures.DecodeByteArray65536(reader);           
            r.MessageHMAC = HMAC.Decode(reader);
            return r;
        }


        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpDmpPacketTypes.MessageStart);
            DirectChannelToken32.Encode(writer);
            writer.Write(MessageId32);
            byte flags = 0;
            writer.Write(flags);
            writer.Write(MessageTimestamp64);
            PacketProcedures.EncodeByteArray65536(writer, EncryptedMessageData);
            MessageHMAC.Encode(writer);

            var r = ms.ToArray();
            if (r.Length > 500) throw new ArgumentException();
            return r;
        }

        public void GetSignedFieldsForMessageHMAC(BinaryWriter writer, bool includeEncryptedMessageData)
        {
            DirectChannelToken32.Encode(writer);
            writer.Write(MessageId32);
            writer.Write(MessageTimestamp64);
            if (includeEncryptedMessageData)
                writer.Write(EncryptedMessageData);
        }
    }
}
