using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {
        internal async Task AcceptInviteRequestAsync(InviteSynPacket syn)
        {
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            _engine.RecentUniqueInviteRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);
        }
    }
}
