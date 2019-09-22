using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DMP.Packets
{
    class MessagePartPacket
    {
        public uint MessageId32;
        UserCertificateSignature SenderSignature;
        MessageSessionStatusCode SenderStatus;
        public HMAC MessageHMAC;
    }
}
