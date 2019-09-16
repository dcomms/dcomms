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
        /// protects local per from processing same (retransmitted) REQ packet, also from spam from a single requester
        /// protects the P2P network against looped REQ requests
        /// </summary>
        HashSet<RegistrationId> _pendingInviteRequests = new HashSet<RegistrationId>();

        /// <summary>
        /// Timestamp32S, SenderToken32 and SenderHMAC are verified at this time
        /// </summary>
        internal async Task ProxyInviteRequestAsync(InviteRequestPacket syn, ConnectionToNeighbor requester, ConnectionToNeighbor responder)
        {
            _engine.WriteToLog_inv_proxySide_detail($"proxying invite");

            _engine.RecentUniqueInviteRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            
            if (syn.NumberOfHopsRemaining <= 1)
            {
                SendNextHopAckResponseToReq(syn, requester, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero);
                return;
            }

            _pendingInviteRequests.Add(syn.RequesterPublicKey);
            try
            {
                // send nhack
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to REQ requester");
                SendNextHopAckResponseToReq(syn, requester);

                syn.NumberOfHopsRemaining--;

                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ   
                var synUdpData = syn.Encode_SetP2pFields(responder);
                await responder.SendUdpRequestAsync_Retransmit_WaitForNPACK(synUdpData,
                    syn.NpaSeq16, syn.GetSignedFieldsForSenderHMAC);

                #region wait for SYNACK from responder  verify SenderHMAC
                _engine.WriteToLog_inv_proxySide_detail($"waiting for SYNACK from responder");
                var inviteSynAckPacketData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(responder.RemoteEndpoint,
                                InviteAck1Packet.GetScanner(syn, responder),
                                    _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                                ));
                if (inviteSynAckPacketData == null) throw new DrpTimeoutException("Did not receive SYNACK on timeout");
                var synAck = InviteAck1Packet.Decode(inviteSynAckPacketData);
                _engine.WriteToLog_inv_proxySide_detail($"verified SYNACK from responder");
                
                // respond with NPACK
                SendNextHopAckResponseToAck1(synAck, responder);
                #endregion

                #region send SYNACK to requester , wait for NPACK and ACK1
                var synAckUdpData = synAck.Encode_SetP2pFields(requester);

                _engine.WriteToLog_inv_proxySide_detail($"sending SYNACK, awaiting for NPACK");
                _ = _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(synAckUdpData, requester.RemoteEndpoint,
                    synAck.NpaSeq16, requester, synAck.GetSignedFieldsForSenderHMAC);
                // not waiting for NPACK, wait for ACK1
                _engine.WriteToLog_inv_proxySide_detail($"waiting for ACK1");

                var ack1UdpData = await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requester.RemoteEndpoint, 
                    InviteAck2Packet.GetScanner(syn, requester));
                _engine.WriteToLog_inv_proxySide_detail($"received ACK1");
                var ack1 = InviteAck2Packet.Decode(ack1UdpData);
                #endregion

                // send NPACK to ACK1
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to ACK1 to requester");
                SendNextHopAckResponseToAck2(ack1, requester);

                // send ACK1 to responder
                // put ACK1.NpaSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                var ack1UdpDataTx = ack1.Encode_SetP2pFields(responder);
                _engine.WriteToLog_inv_proxySide_detail($"sending ACK1 to responder");
                await responder.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack1UdpDataTx,
                    ack1.NpaSeq16, ack1.GetSignedFieldsForSenderHMAC);
                _engine.WriteToLog_inv_proxySide_detail($"received NPACK to ACK1 from responder");

                // wait for ACK2 from responder
                _engine.WriteToLog_inv_proxySide_detail($"waiting for ACK2 from responder");
                var ack2PacketData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(responder.RemoteEndpoint,
                                InviteConfirmationPacket.GetScanner(syn, responder),
                                    _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                                ));
                if (ack2PacketData == null) throw new DrpTimeoutException("Did not receive ACK2 on timeout");
                var ack2 = InviteConfirmationPacket.Decode(ack2PacketData);
                // todo verify signature, updte RDRs and QoS here
                _engine.WriteToLog_inv_proxySide_detail($"verified ACK2 from responder");

                // send ACK2 to requester
                var ack2PacketDataTx = ack2.Encode_SetP2pFields(requester);

                _engine.WriteToLog_inv_proxySide_detail($"sending ACK2 to requester, waiting for NPACK");
                await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack2PacketDataTx, requester.RemoteEndpoint,
                    ack2.NpaSeq16, requester, ack2.GetSignedFieldsForSenderHMAC);
                _engine.WriteToLog_inv_proxySide_detail($"received NPACK to ACK2 from requester");
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
