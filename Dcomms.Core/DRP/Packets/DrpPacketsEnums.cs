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
        RegisterPow1RequestPacket = 1,
        RegisterPow1ResponsePacket = 2,
        RegisterSynPacket = 3,
        NextHopResponsePacket = 4,
        RegisterSynAckPacket = 5,
        RegisterAckPacket = 6,
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
