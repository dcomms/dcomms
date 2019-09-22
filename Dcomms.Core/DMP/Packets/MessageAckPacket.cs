﻿using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DMP.Packets
{
    class MessageAckPacket
    {
        public uint MessageId32;
        public byte[] ReceiverFinalNonce;
        public MessageSessionStatusCode ReceiverStatus;
        public HMAC MessageHMAC;
    }
}