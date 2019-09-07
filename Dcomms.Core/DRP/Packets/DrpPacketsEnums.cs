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
        RegisterSyn = 3,
        NextHopAck = 4,
        RegisterSynAck = 5,
        RegisterAck = 6,
        Ping = 7,
        Pong = 8,
        RegisterConfirmation = 9,
        InviteSyn = 10,
        InviteSynAck = 11,
        InviteAck = 12,
        InviteConfirmation = 13
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
