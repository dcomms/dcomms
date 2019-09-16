using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {
        /// <summary>
        /// list of currently proxied or accepted INVITE requester regIDs
        /// protects local per from processing same (retransmitted) SYN packet, also from spam from a single requester
        /// protects the P2P network against looped SYN requests
        /// </summary>
        HashSet<RegistrationPublicKey> _pendingInviteRequests = new HashSet<RegistrationPublicKey>();

        internal async Task ProxyInviteRequestAsync(InviteSynPacket syn, ConnectionToNeighbor requester, ConnectionToNeighbor responder)
        {
            _engine.WriteToLog_inv_proxySide_detail($"proxying invite");

            _engine.RecentUniqueInviteRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);

            if (!_engine.ValidateReceivedSynTimestamp32S(syn.Timestamp32S))
                throw new BadSignatureException();

            if (syn.NumberOfHopsRemaining <= 1)
            {
                SendNextHopAckResponseToSyn(syn, requester, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero);
                return;
            }

            _pendingInviteRequests.Add(syn.RequesterPublicKey);
            try
            {
                // send nhack
                _engine.WriteToLog_inv_proxySide_detail($"sending NHACK to SYN requester");
                SendNextHopAckResponseToSyn(syn, requester);

                syn.NumberOfHopsRemaining--;

                // send (proxy) SYN to responder. wait for NHACK, verify NHACK.senderHMAC, retransmit SYN   
                var synUdpData = syn.Encode_SetP2pFields(responder);
                await responder.SendUdpRequestAsync_Retransmit_WaitForNHACK(synUdpData,
                    syn.NhaSeq16, syn.GetSignedFieldsForSenderHMAC);

                #region wait for SYNACK from responder  verify SenderHMAC
                _engine.WriteToLog_inv_proxySide_detail($"waiting for SYNACK from responder");
                var inviteSynAckPacketData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(responder.RemoteEndpoint,
                                InviteSynAckPacket.GetScanner(syn, responder),
                                    _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                                ));
                if (inviteSynAckPacketData == null) throw new DrpTimeoutException("Did not receive SYNACK on timeout");
                var synAck = InviteSynAckPacket.Decode(inviteSynAckPacketData);
                _engine.WriteToLog_inv_proxySide_detail($"verified SYNACK from responder");
                
                // respond with NHACK
                SendNextHopAckResponseToSynAck(synAck, responder);
                #endregion

                #region send SYNACK to requester , wait for NHACK and ACK1
                var synAckUdpData = synAck.Encode_SetP2pFields(requester);

                _engine.WriteToLog_inv_proxySide_detail($"sending SYNACK, awaiting for NHACK");
                _ = _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNextHopAck(synAckUdpData, requester.RemoteEndpoint,
                    synAck.NhaSeq16, requester, synAck.GetSignedFieldsForSenderHMAC);
                // not waiting for NHACK, wait for ACK1
                _engine.WriteToLog_inv_proxySide_detail($"waiting for ACK1");

                var ack1UdpData = await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requester.RemoteEndpoint, 
                    InviteAck1Packet.GetScanner(syn, requester));
                _engine.WriteToLog_inv_proxySide_detail($"received ACK1");
                var ack1 = InviteAck1Packet.Decode(ack1UdpData);
                #endregion

                // send NHACK to ACK1
                _engine.WriteToLog_inv_proxySide_detail($"sending NHACK to ACK1 to requester");
                SendNextHopAckResponseToAck1(ack1, requester);

                // send ACK1 to responder
                // put ACK1.NhaSeq16, sendertoken32, senderHMAC  
                // wait for NHACK
                var ack1UdpDataTx = ack1.Encode_SetP2pFields(responder);
                _engine.WriteToLog_inv_proxySide_detail($"sending ACK1 to responder");
                await responder.SendUdpRequestAsync_Retransmit_WaitForNHACK(ack1UdpDataTx,
                    ack1.NhaSeq16, ack1.GetSignedFieldsForSenderHMAC);
                _engine.WriteToLog_inv_proxySide_detail($"received NHACK to ACK1 from responder");

                // wait for ACK2 from responder
                _engine.WriteToLog_inv_proxySide_detail($"waiting for ACK2 from responder");
                var ack2PacketData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(responder.RemoteEndpoint,
                                InviteAck2Packet.GetScanner(syn, responder),
                                    _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                                ));
                if (ack2PacketData == null) throw new DrpTimeoutException("Did not receive ACK2 on timeout");
                var ack2 = InviteAck2Packet.Decode(ack2PacketData);
                // todo verify signature, updte RDRs and QoS here
                _engine.WriteToLog_inv_proxySide_detail($"verified ACK2 from responder");

                // send ACK2 to requester
                var ack2PacketDataTx = ack2.Encode_SetP2pFields(requester);

                _engine.WriteToLog_inv_proxySide_detail($"sending ACK2 to requester, waiting for NHACK");
                await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNextHopAck(ack2PacketDataTx, requester.RemoteEndpoint,
                    ack2.NhaSeq16, requester, ack2.GetSignedFieldsForSenderHMAC);
                _engine.WriteToLog_inv_proxySide_detail($"received NHACK to ACK2 from requester");
            }
            catch (Exception exc)
            {
                _engine.HandleExceptionWhileProxyingInvite(exc);
            }
            finally
            {
                _pendingInviteRequests.Remove(syn.RequesterPublicKey);
            }
        }
    }
}
