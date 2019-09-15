using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {
        void ProxyInvite(InviteSynPacket syn)
        {
            _engine.RecentUniqueInviteRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);
        }
    }
}
