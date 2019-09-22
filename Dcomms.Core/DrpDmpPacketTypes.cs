using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms
{
    enum DrpDmpPacketTypes
    {
        RegisterPow1Request = 1,
        RegisterPow1Response = 2,
        RegisterReq = 3,
        NeighborPeerAck = 4,
        RegisterAck1 = 5,
        RegisterAck2 = 6,
        Ping = 7,
        Pong = 8,
        RegisterConfirmation = 9,
        InviteReq = 10,
        InviteAck1 = 11,
        InviteAck2 = 12,
        InviteCfm = 13,

        DmpPing = 20,
        DmpPong = 21,
        MessageStart = 22,
        MessagePart = 23,
        MessageAck = 24
    }
}
