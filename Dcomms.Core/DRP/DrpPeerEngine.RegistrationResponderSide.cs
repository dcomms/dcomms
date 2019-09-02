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
        async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RegisterSynPacket syn, IPEndPoint requesterEndpoint) // engine thread
        {
            WriteToLog_reg_responderSide_detail($"accepting registration from {requesterEndpoint}: NhaSeq16={syn.NhaSeq16}, epEndpoint={syn.EpEndpoint}");
           
            _pendingRegisterRequests.Add(syn.RequesterPublicKey_RequestID);
            try
            {               
                if (syn.AtoEP == false)
                {
                    //todo check hmac of proxy sender
                    throw new NotImplementedException();
                }

                // check signature of requester (A)
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

                SendNextHopAckResponseToSyn(syn, requesterEndpoint);

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer)
                {
                    LocalEndpoint = syn.EpEndpoint,
					RemotePeerPublicKey = syn.RequesterPublicKey_RequestID					
                };
                byte[] registerSynAckUdpPayload;
                try
                {
                    var synAck = new RegisterSynAckPacket
                    {                        
                        RequesterPublicKey_RequestID = syn.RequesterPublicKey_RequestID,
                        RegisterSynTimestamp32S = syn.Timestamp32S,
                        ResponderEcdhePublicKey = new EcdhPublicKey { ecdh25519PublicKey = newConnectionToNeighbor.LocalEcdhe25519PublicKey },
                        ResponderPublicKey = acceptAt.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                        ResponderStatusCode = DrpResponderStatusCode.confirmed,
                        NhaSeq16 = GetNewNhaSeq16_AtoEP(),
                    };
                    synAck.ToResponderTxParametersEncrypted = newConnectionToNeighbor.EncryptAtRegisterResponder(syn, synAck);
                    synAck.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w2 => synAck.GetCommonRequesterProxierResponderFields(w2, false, true),
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (syn.AtoEP) synAck.RequesterEndpoint = requesterEndpoint;

                    registerSynAckUdpPayload = synAck.EncodeAtResponder(null);
                    SendPacket(registerSynAckUdpPayload, requesterEndpoint);
                    WriteToLog_reg_responderSide_detail($"sent SYNACK");


                    var regAckScanner = RegisterAckPacket.GetScanner(null, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                    RegisterAckPacket registerAckPacket;
                    if (syn.AtoEP)
                    { // wait for reg ACK, retransmitting SynAck
                        WriteToLog_reg_responderSide_detail($"waiting for ACK");
                        var regAckUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(registerSynAckUdpPayload, requesterEndpoint, regAckScanner);
                        WriteToLog_reg_responderSide_detail($"received ACK");
                        registerAckPacket = RegisterAckPacket.DecodeAndVerify_OptionallyInitializeP2pStreamAtResponder(
                            regAckUdpPayload, syn, synAck, newConnectionToNeighbor); // verifies hmac, decrypts endpoint of A
                    }
                    else
                    {// todo  retransmit until NHACK; at same time (!!!)  wait for regACK
                        throw new NotImplementedException(); 
                    }


                    WriteToLog_reg_responderSide_detail($"verified ACK");
                    acceptAt.ConnectedPeers.Add(newConnectionToNeighbor); // added to list here in order to respond to ping requests from A                    
                    SendNextHopAckResponseToAck(registerAckPacket, requesterEndpoint); // send NHACK to ACK

                    _ = WaitForRegistrationConfirmationRequestAsync(requesterEndpoint, syn, newConnectionToNeighbor);

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
                catch
                {
                    newConnectionToNeighbor.Dispose();
                    throw;
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
        async Task WaitForRegistrationConfirmationRequestAsync(IPEndPoint requesterEndpoint, RegisterSynPacket syn, ConnectionToNeighbor newConnectionToNeighbor)
        {
            try
            {
                var regCfmScanner = RegisterConfirmationPacket.GetScanner(null, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                WriteToLog_reg_responderSide_detail($"waiting for CFM");
                var regCfmUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, requesterEndpoint, regCfmScanner);
                WriteToLog_reg_responderSide_detail($"received CFM");
                var registerCfmPacket = RegisterConfirmationPacket.DecodeAndOptionallyVerify(regCfmUdpPayload, syn, newConnectionToNeighbor);
                WriteToLog_reg_responderSide_detail($"verified CFM");

                SendNextHopAckResponseToCfm(registerCfmPacket, requesterEndpoint);
                WriteToLog_reg_responderSide_detail($"sent NHACK to CFM");
            }
			catch (Exception exc)
            {
                newConnectionToNeighbor.Dispose();
                HandleExceptionInRegistrationResponder(requesterEndpoint, exc);
            }
        }

        void SendNextHopAckResponseToSyn(RegisterSynPacket syn, IPEndPoint requesterEndpoint, NextHopResponseCode statusCode = NextHopResponseCode.accepted)
        {
            var nextHopAck = new NextHopAckPacket
            {
                NhaSeq16 = syn.NhaSeq16,
                StatusCode = statusCode
            };
            if (syn.AtoEP == false)
            {
                //  nextHopAckPacket.SenderToken32 = x;
                //  nextHopAckPacket.SenderHMAC = x;
                throw new NotImplementedException();
            }
            var nextHopAckPacketData = nextHopAck.Encode(syn.AtoEP);
            
            RespondToRequestAndRetransmissions(syn.OriginalUdpPayloadData, nextHopAckPacketData, requesterEndpoint);

            //   WriteToLog_reg_responderSide_detail($"sent nextHopAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(nextHopAckPacketData)} nhaSeq={registerSynPacket.NhaSeq16}");
        }
        void SendNextHopAckResponseToSynAck(RegisterSynAckPacket synAck, ConnectionToNeighbor receivedSynAckFromNeighbor)
        {
            var nextHopAck = new NextHopAckPacket
            {
                NhaSeq16 = synAck.NhaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };

            nextHopAck.SenderToken32 = receivedSynAckFromNeighbor.RemotePeerToken32;
            nextHopAck.SenderHMAC = receivedSynAckFromNeighbor.GetSharedHMAC(w => nextHopAck.GetFieldsForHMAC(w, synAck.GetSignedFieldsForSenderHMAC));
            var nextHopAckPacketData = nextHopAck.Encode(false);

            RespondToRequestAndRetransmissions(synAck.OriginalUdpPayloadData, nextHopAckPacketData, receivedSynAckFromNeighbor.RemoteEndpoint);

            //   WriteToLog_reg_responderSide_detail($"sent nextHopAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(nextHopAckPacketData)} nhaSeq={registerSynPacket.NhaSeq16}");
        }
        void SendNextHopAckResponseToAck(RegisterAckPacket ack, IPEndPoint remoteEndpoint)
        {
            var nextHopAckPacket = new NextHopAckPacket
            {
                NhaSeq16 = ack.NhaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (ack.AtoEP == false)
            {
                //  nextHopAckPacket.SenderToken32 = x;
                //  nextHopAckPacket.SenderHMAC = x;
                throw new NotImplementedException();
            }
            var nextHopAckPacketData = nextHopAckPacket.Encode(ack.AtoEP);
            RespondToRequestAndRetransmissions(ack.OriginalUdpPayloadData, nextHopAckPacketData, remoteEndpoint);
        }
        void SendNextHopAckResponseToCfm(RegisterConfirmationPacket cfm, IPEndPoint remoteEndpoint)
        {
            var nextHopAckPacket = new NextHopAckPacket
            {
                NhaSeq16 = cfm.NhaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (cfm.AtoEP == false)
            {
                //  nextHopAckPacket.SenderToken32 = x;
                //  nextHopAckPacket.SenderHMAC = x;
                throw new NotImplementedException();
            }
            var nextHopAckPacketData = nextHopAckPacket.Encode(cfm.AtoEP);
            RespondToRequestAndRetransmissions(cfm.OriginalUdpPayloadData, nextHopAckPacketData, remoteEndpoint);
        }

        bool ValidateReceivedSynTimestamp32S(uint receivedSynTimestamp32S)
        {
            var differenceS = Math.Abs((int)receivedSynTimestamp32S - Timestamp32S);
            return differenceS < Configuration.MaxSynTimestampDifference;
        }
        /// <summary>
        /// protects local per from processing same (retransmitted) SYN packet
        /// protects the P2P network against looped SYN requests
        /// </summary>
        HashSet<RegistrationPublicKey> _pendingRegisterRequests = new HashSet<RegistrationPublicKey>();
      
        //  internal Dictionary<RegistrationPublicKey, PendingAcceptedRegisterRequest> PendingAcceptedRegisterRequests => _pendingAcceptedRegisterRequests;
  //      void PendingAcceptedRegisterRequests_OnTimer100ms(DateTime timeNowUTC)
  //      {           
		////_loop:
  ////          foreach (var r in _pendingAcceptedRegisterRequests.Values)
  ////          {
  ////              r.OnTimer_100ms(timeNowUTC, out var needToRestartLoop); //   clean timed out requests
  ////              if (needToRestartLoop) goto _loop;
  ////          }
  //      }
    }
}
