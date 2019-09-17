using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{
    enum DrpPacketType
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
        InviteCfm = 13
    }
          
    public enum DrpResponderStatusCode
    {
        confirmed,

        rejected_badSenderRating,
        rejected_badtimestamp,
        rejected_maxhopsReached,
        rejected_noGoodPeers, // timed out or dead end in IDspace
        rejected_userBusyForInvite
    }

}
