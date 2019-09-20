using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DMP.Packets
{
    class MessageStartPacket
    {
        public DirectChannelToken32 DirectChannelToken32;
        public uint MessageId32;

        public UInt64 MessageTimestamp64;
        public byte[] EncryptedData;

        public HMAC MessageSessionHmac;
    }
}
