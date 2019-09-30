using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{              
    public enum DrpResponderStatusCode
    {
        confirmed = 0,

     //   rejected_badSenderRating,
     //   rejected_badtimestamp,
        rejected_maxhopsReached = 1,
     //   rejected_noGoodPeers, // timed out or dead end in IDspace
     //   rejected_userBusyForInvite
    }

}
