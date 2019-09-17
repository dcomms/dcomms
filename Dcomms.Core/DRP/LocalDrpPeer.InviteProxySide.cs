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
        /// Timestamp32S, NeighborToken32 and NeighborHMAC are verified at this time
        /// </summary>
        internal async Task ProxyInviteRequestAsync(InviteRequestPacket req, ConnectionToNeighbor sourcePeer, ConnectionToNeighbor destinationPeer)
        {
            _engine.WriteToLog_inv_proxySide_detail($"proxying invite");

            _engine.RecentUniqueInviteRequests.AssertIsUnique(req.GetUniqueRequestIdFields);
            _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            
            if (req.NumberOfHopsRemaining <= 1)
            {
                SendNeighborPeerAckResponseToReq(req, sourcePeer, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero);
                return;
            }

            _pendingInviteRequests.Add(req.RequesterRegistrationId);
            try
            {
                // send npack
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to REQ source peer");
                SendNeighborPeerAckResponseToReq(req, sourcePeer);

                req.NumberOfHopsRemaining--;

                // send (proxy) REQ to responder. wait for NPACK, verify NPACK.senderHMAC, retransmit REQ   
                var reqUdpData = req.Encode_SetP2pFields(destinationPeer);
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(reqUdpData, req.NpaSeq16, req.GetSignedFieldsForNeighborHMAC);

                #region wait for ACK1 from responder  verify NeighborHMAC
                _engine.WriteToLog_inv_proxySide_detail($"waiting for ACK1 from responder");
                var ack1UdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                InviteAck1Packet.GetScanner(req, destinationPeer),
                                    _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                                ));
                if (ack1UdpData == null) throw new DrpTimeoutException("Did not receive ACK1 on timeout");
                var ack1 = InviteAck1Packet.Decode(ack1UdpData);
                _engine.WriteToLog_inv_proxySide_detail($"verified ACK1 from responder");
                
                // respond with NPACK
                SendNeighborPeerAckResponseToAck1(ack1, destinationPeer);
                #endregion

                #region send ACK1 to requester, wait for NPACK and ACK2
                var ack1UdpDataTx = ack1.Encode_SetP2pFields(sourcePeer);

                _engine.WriteToLog_inv_proxySide_detail($"sending ACK1, awaiting for NPACK");
                _ = _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack1UdpDataTx, sourcePeer.RemoteEndpoint,
                    ack1.NpaSeq16, sourcePeer, ack1.GetSignedFieldsForNeighborHMAC);
                // not waiting for NPACK, wait for ACK1
                _engine.WriteToLog_inv_proxySide_detail($"waiting for ACK2");

                var ack2UdpData = await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, sourcePeer.RemoteEndpoint, 
                    InviteAck2Packet.GetScanner(req, sourcePeer));
                _engine.WriteToLog_inv_proxySide_detail($"received ACK2");
                var ack2 = InviteAck2Packet.Decode(ack2UdpData);
                #endregion

                // send NPACK to ACK2
                _engine.WriteToLog_inv_proxySide_detail($"sending NPACK to ACK2 to source peer");
                SendNeighborPeerAckResponseToAck2(ack2, sourcePeer);

                // send ACK2 to responder
                // put ACK2.NpaSeq16, sendertoken32, senderHMAC  
                // wait for NPACK
                var ack2UdpDataTx = ack2.Encode_SetP2pFields(destinationPeer);
                _engine.WriteToLog_inv_proxySide_detail($"sending ACK2 to responder");
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNPACK(ack2UdpDataTx,
                    ack2.NpaSeq16, ack2.GetSignedFieldsForNeighborHMAC);
                _engine.WriteToLog_inv_proxySide_detail($"received NPACK to ACK2 from destination peer");

                // wait for CFM from responder
                _engine.WriteToLog_inv_proxySide_detail($"waiting for CFM from responder");
                var cfmUdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(destinationPeer.RemoteEndpoint,
                                InviteConfirmationPacket.GetScanner(req, destinationPeer),
                                    _engine.DateTimeNowUtc, _engine.Configuration.InviteRequestsTimoutS
                                ));
                if (cfmUdpData == null) throw new DrpTimeoutException("Did not receive CFM on timeout");
                var cfm = InviteConfirmationPacket.Decode(cfmUdpData);
                // todo verify signature, update RDRs and QoS
                _engine.WriteToLog_inv_proxySide_detail($"verified CFM from responder");

                // send CFM to requester
                var cfmUdpDataTx = cfm.Encode_SetP2pFields(sourcePeer);

                _engine.WriteToLog_inv_proxySide_detail($"sending CFM to requester, waiting for NPACK");
                await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(cfmUdpDataTx, sourcePeer.RemoteEndpoint,
                    cfm.NpaSeq16, sourcePeer, cfm.GetSignedFieldsForNeighborHMAC);
                _engine.WriteToLog_inv_proxySide_detail($"received NPACK to CFM from source peer");
            }
            catch (Exception exc)
            {
                _engine.HandleExceptionWhileProxyingInvite(exc);
            }
            finally
            {
                _pendingInviteRequests.Remove(req.RequesterRegistrationId);
            }
        }
    }
}
