using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {
        void TryBeginAcceptRegisterRequest(LocalDrpPeer acceptAt, RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint, DateTime requestReceivedAtUtc)
        {
            if (_pendingAcceptedRegisterRequests.ContainsKey(registerSynPacket.RequesterPublicKey_RequestID) == false)
            {
                if (registerSynPacket.AtoRP == false)
                {
                    //todo check hmac of proxy sender
                    throw new NotImplementedException();
                }

                // check signature of requester (A)
                if (!ValidateReceivedTimestamp32S(registerSynPacket.Timestamp32S) ||
						!registerSynPacket.RequesterSignature.Verify(_cryptoLibrary,
							w => registerSynPacket.GetCommonRequesterAndResponderFields(w, false),
							registerSynPacket.RequesterPublicKey_RequestID
						)
					)
                {
                    OnReceivedBadSignature(remoteEndpoint);
                    return;
                }
				

                // respond with NextHopAckPacket   
                var nextHopAckPacket = new NextHopAckPacket
                {
					NhaSeq16 = registerSynPacket.NhaSeq16,					
					StatusCode = NextHopResponseCode.accepted
                };
				if (registerSynPacket.AtoRP == false)
                {
                    //  nextHopAckPacket.SenderToken32 = x;
                    //  nextHopAckPacket.SenderHMAC = x;
                    throw new NotImplementedException();
                }
                SendPacket(nextHopAckPacket.Encode(registerSynPacket.AtoRP), remoteEndpoint);
				
                _cryptoLibrary.GenerateEcdh25519Keypair(out var localEcdhe25519PrivateKey, out var localEcdhe25519PublicKey);

                var registerSynAckPacket = new RegisterSynAckPacket
                {
                    NeighborEcdhePublicKey = new EcdhPublicKey { ecdh25519PublicKey = localEcdhe25519PublicKey },             
                    NeighborPublicKey = acceptAt.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    NeighborStatusCode = DrpResponderStatusCode.confirmed,
                    NhaSeq16 = GetNewNhaSeq16(),
                    RegisterSynTimestamp32S = registerSynPacket.Timestamp32S
                };         
                registerSynAckPacket.ToNeighborTxParametersEncrypted = P2pStreamParameters.EncryptAtRegisterResponder(localEcdhe25519PrivateKey, registerSynPacket, registerSynAckPacket, );
                registerSynAckPacket.NeighborSignature = RegistrationSignature.Sign(_cryptoLibrary, 
					w => registerSynAckPacket.GetCommonRequesterAndResponderFields(w, false, true), 
					acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                if (registerSynPacket.AtoRP) registerSynAckPacket.RequesterEndpoint = remoteEndpoint;

                var registerSynAckUdpPayload = registerSynAckPacket.EncodeAtResponder(null);
                SendPacket(registerSynAckUdpPayload, remoteEndpoint);
				
                var pendingReqest = new PendingAcceptedRegisterRequest(registerSynPacket, registerSynAckPacket, registerSynAckUdpPayload, localEcdhe25519PrivateKey, requestReceivedAtUtc);
                _pendingAcceptedRegisterRequests.Add(registerSynPacket.RequesterPublicKey_RequestID, pendingReqest);
            }
            // else it is a duplicate reg SYN request
        }
		bool ValidateReceivedTimestamp32S(uint receivedTimestamp32S)
        {
            var differenceS = Math.Abs((int)receivedTimestamp32S - Timestamp32S);
            return differenceS < Configuration.Timestamp32S_MaxDifferenceToAccept;
        }
        Dictionary<RegistrationPublicKey, PendingAcceptedRegisterRequest> _pendingAcceptedRegisterRequests = new Dictionary<RegistrationPublicKey, PendingAcceptedRegisterRequest>();
		void PendingAcceptedRegisterRequests_OnTimer100ms(DateTime timeNowUTC)
        {
            //   clean timed out requests, raise timeout events
            foreach (var r in _pendingAcceptedRegisterRequests.Values)
            {
                r.OnTimer_100ms(timeNowUTC, out var needToRestartLoop);

            }
        }
    }
}
