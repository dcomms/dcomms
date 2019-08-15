using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {

        void TryBeginAcceptRegisterRequest(LocalDrpPeer acceptAt, RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint)
        {
            if (_pendingAcceptedRegisterRequests.ContainsKey(registerSynPacket.RequesterPublicKey_RequestID) == false)
            {
                // respond with NextHopAckPacket   
                var nextHopAckPacket = new NextHopAckPacket();
                SendPacket(nextHopAckPacket);

                //todo check hmac of proxy sender

                // todo check signature of A

                //todo encrypt ToNeighborTxParametersEncrypted

                var registerSynAckPacket = new RegisterSynAckPacket
                {
                    NeighborEcdhePublicKey = x,
                    NeighborPublicKey = acceptAt.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    NeighborSignature = x,
                    NeighborStatusCode = x,
                    NhaSeq16 = x,
                    RegisterSynTimestamp32S = registerSynPacket.Timestamp32S,     
                    ToNeighborTxParametersEncrypted = x,
                };
                if (registerSynPacket.AtoRP) registerSynAckPacket.RequesterEndpoint = remoteEndpoint;

                SendPacket(registerSynAckPacket.Encode(), remoteEndpoint);

                //todo retransmit it until NextHopAckPacket   (if sent from proxier)        or until RegAck (if SYN was sent by A)
				
                var pendingReqest = new PendingAcceptedRegisterRequest(registerSynPacket, DateTimeNowUtc);
                _pendingAcceptedRegisterRequests.Add(registerSynPacket.RequesterPublicKey_RequestID, pendingReqest);
            }
        }
        Dictionary<RegistrationPublicKey, PendingAcceptedRegisterRequest> _pendingAcceptedRegisterRequests = new Dictionary<RegistrationPublicKey, PendingAcceptedRegisterRequest>();
		void CleanPendingAcceptedRegisterRequests_Timer100ms(DateTime timeNowUTC)
        {
			x
        }
    }
}
