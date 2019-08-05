using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{
    class PingRequestPacket
    {
        RemotePeerToken32 SenderToken32;
        byte ReservedFlagsMustBeZero;
        ushort Timestamp; // msec
        float? MaxRxInviteRateRps;   // signal from sender "how much I can receive via this p2p connection"
        float? MaxRxRegisterRateRps; // signal from sender "how much I can receive via this p2p connection"
        HMAC SenderHMAC;
    }
    class PingResponsePacket
    {
        RemotePeerToken32 SenderToken32;
        byte ReservedFlagsMustBeZero;
        ushort TimestampOfRequest; // msec
        HMAC SenderHMAC;
    }
}
