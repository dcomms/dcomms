using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {
        /// <summary>
        /// main register responder proc for both A-EP and P2P modes
        /// in P2P mode Timestamp32S, SenderToken32 and SenderHMAC are verified at this time
        /// </summary>
        /// <param name="receivedFromInP2pMode">
        /// is null in A-EP mode
        /// </param>
        internal async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RegisterRequestPacket syn, IPEndPoint requesterEndpoint, ConnectionToNeighbor synReceivedFromInP2pMode) // engine thread
        {
            WriteToLog_reg_responderSide_detail($"accepting registration from {requesterEndpoint}: NpaSeq16={syn.NpaSeq16}, NumberOfHopsRemaining={syn.NumberOfHopsRemaining}, epEndpoint={syn.EpEndpoint}, synReceivedFromInP2pMode={synReceivedFromInP2pMode}");

            if (syn.AtoEP ^ (synReceivedFromInP2pMode == null))
                throw new InvalidOperationException();

            if (synReceivedFromInP2pMode == null)
            {
                // check Timestamp32S and signature of requester (A)
                if (!ValidateReceivedSynTimestamp32S(syn.Timestamp32S) ||
                        !syn.RequesterSignature.Verify(_cryptoLibrary,
                            w => syn.GetCommonRequesterProxyResponderFields(w, false),
                            syn.RequesterPublicKey_RequestID
                        )
                    )
                    throw new BadSignatureException();
                if (syn.EpEndpoint.Address.Equals(acceptAt.PublicIpApiProviderResponse) == false)
                {
                    throw new PossibleMitmException();
                }
            }

            RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
            _recentUniqueRegistrationRequests.AssertIsUnique(syn.GetUniqueRequestIdFields);

            _pendingRegisterRequests.Add(syn.RequesterPublicKey_RequestID);
            try
            {

                WriteToLog_reg_responderSide_detail($"sending NPACK to REQ to {requesterEndpoint}");

                SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.accepted, synReceivedFromInP2pMode);

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer)
                {
                    LocalEndpoint = synReceivedFromInP2pMode?.LocalEndpoint ?? syn.EpEndpoint,
					RemotePeerPublicKey = syn.RequesterPublicKey_RequestID					
                };
                byte[] registerSynAckUdpPayload;
                try
                {
                    var synAck = new RegisterAck1Packet
                    {                        
                        RequesterPublicKey_RequestID = syn.RequesterPublicKey_RequestID,
                        RegisterSynTimestamp32S = syn.Timestamp32S,
                        ResponderEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                        ResponderPublicKey = acceptAt.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                        ResponderStatusCode = DrpResponderStatusCode.confirmed,
                        NpaSeq16 = GetNewNhaSeq16_AtoEP(),
                    };
                    RecentUniquePublicEcdhKeys.AssertIsUnique(synAck.ResponderEcdhePublicKey.Ecdh25519PublicKey);
                    synAck.ToResponderTxParametersEncrypted = newConnectionToNeighbor.Encrypt_synack_ToResponderTxParametersEncrypted_AtResponder_DeriveSharedDhSecret(syn, synAck, synReceivedFromInP2pMode);
                    synAck.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        (w2) =>
                        {
                            syn.GetCommonRequesterProxyResponderFields(w2, true);
                            synAck.GetCommonRequesterProxierResponderFields(w2, false, true);
                        },
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (synReceivedFromInP2pMode == null) synAck.RequesterEndpoint = requesterEndpoint;                    
                    registerSynAckUdpPayload = synAck.Encode_OpionallySignSenderHMAC(synReceivedFromInP2pMode);
                    
                    var ackScanner = RegisterAck2Packet.GetScanner(synReceivedFromInP2pMode, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                    byte[] ackUdpData;
                    if (synReceivedFromInP2pMode == null)
                    {   // wait for ACK, retransmitting SYNACK
                        WriteToLog_reg_responderSide_detail($"sending SYNACK, waiting for ACK");
                        ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(registerSynAckUdpPayload, requesterEndpoint, ackScanner);
                    }
                    else
                    {   // retransmit SYNACK until NPACK (via P2P); at same time wait for ACK
                        WriteToLog_reg_responderSide_detail($"sending SYNACK, awaiting for NPACK");
                        _ = OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(registerSynAckUdpPayload, requesterEndpoint,
                            synAck.NpaSeq16, synReceivedFromInP2pMode, synAck.GetSignedFieldsForSenderHMAC);
                        // not waiting for NPACK, wait for ACK
                        WriteToLog_reg_responderSide_detail($"waiting for ACK");                        
                        ackUdpData = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, ackScanner);                
                    }

                    WriteToLog_reg_responderSide_detail($"received ACK");
                    var ack = RegisterAck2Packet.Decode_OptionallyVerify_InitializeP2pStreamAtResponder(
                        ackUdpData, syn, synAck, newConnectionToNeighbor);

                    WriteToLog_reg_responderSide_detail($"verified ACK");
                    acceptAt.ConnectedPeers.Add(newConnectionToNeighbor); // added to list here in order to respond to ping requests from A                    
                    SendNextHopAckResponseToAck(ack, requesterEndpoint, synReceivedFromInP2pMode); // send NPACK to ACK

                    _ = WaitForRegistrationConfirmationRequestAsync(requesterEndpoint, syn, newConnectionToNeighbor, synReceivedFromInP2pMode);

                    #region send ping, verify pong
                    var ping = newConnectionToNeighbor.CreatePing(true);
                    
                    var pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalRxToken32, ping.PingRequestId32), DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    ping.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    WriteToLog_reg_responderSide_detail($"sent ping");
                    var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pong from A
                    if (pongPacketData == null) throw new DrpTimeoutException();
                    var pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                        pongPacketData, ping, newConnectionToNeighbor,
                        true);
                    WriteToLog_reg_responderSide_detail($"verified pong");
                    newConnectionToNeighbor.OnReceivedVerifiedPong(pong, pendingPingRequest.ResponseReceivedAtUtc.Value,
                        pendingPingRequest.ResponseReceivedAtUtc.Value - pendingPingRequest.InitialTxTimeUTC.Value);
                    #endregion
                }
                catch (Exception exc)
                {
                    newConnectionToNeighbor.Dispose();
                    throw exc;
                }
            }
			catch (Exception exc)
            {
                HandleExceptionInRegistrationResponder(requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(syn.RequesterPublicKey_RequestID);
            }
        }
        async Task WaitForRegistrationConfirmationRequestAsync(IPEndPoint requesterEndpoint, RegisterRequestPacket syn, ConnectionToNeighbor newConnectionToNeighbor, ConnectionToNeighbor synReceivedFromInP2pMode)
        {
            try
            {
                var regCfmScanner = RegisterConfirmationPacket.GetScanner(synReceivedFromInP2pMode, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                WriteToLog_reg_responderSide_detail($"waiting for CFM");
                var regCfmUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, regCfmScanner);
                WriteToLog_reg_responderSide_detail($"received CFM");
                var registerCfmPacket = RegisterConfirmationPacket.DecodeAndOptionallyVerify(regCfmUdpPayload, syn, newConnectionToNeighbor);
                WriteToLog_reg_responderSide_detail($"verified CFM");

                SendNextHopAckResponseToCfm(registerCfmPacket, requesterEndpoint, synReceivedFromInP2pMode);
                WriteToLog_reg_responderSide_detail($"sent NPACK to CFM");
            }
			catch (Exception exc)
            {
                newConnectionToNeighbor.Dispose();
                HandleExceptionInRegistrationResponder(requesterEndpoint, exc);
            }
        }

        void SendNextHopAckResponseToSyn(RegisterRequestPacket syn, IPEndPoint requesterEndpoint, NextHopResponseCode statusCode, ConnectionToNeighbor synReceivedFromInP2pMode)
        {
            var nextHopAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = syn.NpaSeq16,
                StatusCode = statusCode
            };
            if (synReceivedFromInP2pMode != null)
            {
                nextHopAck.SenderToken32 = synReceivedFromInP2pMode.RemotePeerToken32;
                nextHopAck.SenderHMAC = synReceivedFromInP2pMode.GetSenderHMAC(w => nextHopAck.GetFieldsForHMAC(w, syn.GetSignedFieldsForSenderHMAC));
            }
            var nextHopAckPacketData = nextHopAck.Encode(synReceivedFromInP2pMode == null);
            
            RespondToRequestAndRetransmissions(syn.DecodedUdpPayloadData, nextHopAckPacketData, requesterEndpoint);

            //   WriteToLog_reg_responderSide_detail($"sent nextHopAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(nextHopAckPacketData)} nhaSeq={registerSynPacket.NpaSeq16}");
        }
        void SendNextHopAckResponseToSynAck(RegisterAck1Packet synAck, ConnectionToNeighbor receivedSynAckFromNeighbor)
        {
            var nextHopAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = synAck.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };

            nextHopAck.SenderToken32 = receivedSynAckFromNeighbor.RemotePeerToken32;
            nextHopAck.SenderHMAC = receivedSynAckFromNeighbor.GetSenderHMAC(w => nextHopAck.GetFieldsForHMAC(w, synAck.GetSignedFieldsForSenderHMAC));
            var nextHopAckPacketData = nextHopAck.Encode(false);

            RespondToRequestAndRetransmissions(synAck.DecodedUdpPayloadData, nextHopAckPacketData, receivedSynAckFromNeighbor.RemoteEndpoint);

            //   WriteToLog_reg_responderSide_detail($"sent nextHopAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(nextHopAckPacketData)} nhaSeq={registerSynPacket.NpaSeq16}");
        }
        void SendNextHopAckResponseToAck(RegisterAck2Packet ack, IPEndPoint remoteEndpoint, ConnectionToNeighbor synReceivedFromInP2pMode)
        {
            var nextHopAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = ack.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (ack.AtoEP == false)
            {
                nextHopAck.SenderToken32 = synReceivedFromInP2pMode.RemotePeerToken32;
                nextHopAck.SenderHMAC = synReceivedFromInP2pMode.GetSenderHMAC(w => nextHopAck.GetFieldsForHMAC(w, ack.GetSignedFieldsForSenderHMAC));
            }

            var nextHopAckPacketData = nextHopAck.Encode(ack.AtoEP);
            RespondToRequestAndRetransmissions(ack.DecodedUdpPayloadData, nextHopAckPacketData, remoteEndpoint);
        }
        void SendNextHopAckResponseToCfm(RegisterConfirmationPacket cfm, IPEndPoint remoteEndpoint, ConnectionToNeighbor synReceivedFromInP2pMode)
        {
            var nextHopAck = new NeighborPeerAckPacket
            {
                NpaSeq16 = cfm.NpaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (cfm.AtoEP == false)
            {
                nextHopAck.SenderToken32 = synReceivedFromInP2pMode.RemotePeerToken32;
                nextHopAck.SenderHMAC = synReceivedFromInP2pMode.GetSenderHMAC(w => nextHopAck.GetFieldsForHMAC(w, cfm.GetSignedFieldsForSenderHMAC));
            }
            var nextHopAckPacketData = nextHopAck.Encode(cfm.AtoEP);
            RespondToRequestAndRetransmissions(cfm.DecodedUdpPayloadData, nextHopAckPacketData, remoteEndpoint);
        }

        internal bool ValidateReceivedSynTimestamp32S(uint receivedSynTimestamp32S)
        {
            var differenceS = Math.Abs((int)receivedSynTimestamp32S - Timestamp32S);
            return differenceS < Configuration.MaxSynTimestampDifference;
        }
        /// <summary>
        /// protects local per from processing same (retransmitted) REQ packet
        /// protects the P2P network against looped REQ requests
        /// </summary>
        HashSet<RegistrationId> _pendingRegisterRequests = new HashSet<RegistrationId>();

    }
}
