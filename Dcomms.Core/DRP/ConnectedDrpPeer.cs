using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    public enum ConnectedDrpPeerInitiatedBy
    {
        localPeer, // local peer connected to remote peer via REGISTER procedure
        remotePeer // remote peer connected to local peer via REGISTER procedure
    }
    public class ConnectedDrpPeer
    {
        public P2pStreamParameters TxParameters;

        public ConnectedDrpPeerInitiatedBy InitiatedBy;
        RegistrationPublicKey RemotePeerPublicKey;

        ConnectedDrpPeerRating Rating;

        IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        float MaxTxInviteRateRps, MaxTxRegiserRaateRps; // sent by remote peer via ping
        IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
        List<TxRegisterRequestState> PendingTxRegisterRequests;
        List<TxInviteRequestState> PendingTxInviteRequests;

        public ConnectedDrpPeer(ConnectedDrpPeerInitiatedBy initiatedBy)
        {
            InitiatedBy = initiatedBy;
        }
    }
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
}
