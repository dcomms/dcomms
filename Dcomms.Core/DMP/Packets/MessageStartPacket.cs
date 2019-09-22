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

        public Int64 MessageTimestamp64;
        public byte[] EncryptedMessageData;

        public HMAC MessageSessionHMAC;


        public void GetSignedFieldsForMessageSessionHMAC(BinaryWriter writer, bool includeEncryptedMessageData)
        {
            DirectChannelToken32.Encode(writer);
            writer.Write(MessageId32);
            writer.Write(MessageTimestamp64);
            if (includeEncryptedMessageData)
                writer.Write(EncryptedMessageData);
        }
    }
}
