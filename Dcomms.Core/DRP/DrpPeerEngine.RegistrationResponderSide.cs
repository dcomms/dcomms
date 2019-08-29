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
        async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint) // engine thread
        {
            WriteToLog_reg_responderSide_detail($"accepting registration: remoteEndpoint={remoteEndpoint}, NhaSeq16={registerSynPacket.NhaSeq16}, epEndpoint={registerSynPacket.EpEndpoint}");
           
            _pendingRegisterRequests.Add(registerSynPacket.RequesterPublicKey_RequestID);
            try
            {               
                if (registerSynPacket.AtoEP == false)
                {
                    //todo check hmac of proxy sender
                    throw new NotImplementedException();
                }

                // check signature of requester (A)
                if (!ValidateReceivedTimestamp32S(registerSynPacket.Timestamp32S) ||
                        !registerSynPacket.RequesterSignature.Verify(_cryptoLibrary,
                            w => registerSynPacket.GetCommonRequesterProxierResponderFields(w, false),
                            registerSynPacket.RequesterPublicKey_RequestID
                        )
                    )
                    throw new BadSignatureException();

                SendNextHopAckResponseToSyn(registerSynPacket, remoteEndpoint);

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer)
                {
                    LocalEndpoint = registerSynPacket.EpEndpoint,
					RemotePeerPublicKey = registerSynPacket.RequesterPublicKey_RequestID					
                };
                byte[] registerSynAckUdpPayload;
                try
                {
                    var registerSynAckPacket = new RegisterSynAckPacket
                    {                        
                        RequesterPublicKey_RequestID = registerSynPacket.RequesterPublicKey_RequestID,
                        RegisterSynTimestamp32S = registerSynPacket.Timestamp32S,
                        ResponderEcdhePublicKey = new EcdhPublicKey { ecdh25519PublicKey = newConnectionToNeighbor.LocalEcdhe25519PublicKey },
                        ResponderPublicKey = acceptAt.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                        ResponderStatusCode = DrpResponderStatusCode.confirmed,
                        NhaSeq16 = GetNewNhaSeq16(),
                    };
                    registerSynAckPacket.ToResponderTxParametersEncrypted = newConnectionToNeighbor.EncryptAtRegisterResponder(registerSynPacket, registerSynAckPacket);
                    registerSynAckPacket.ResponderSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w2 => registerSynAckPacket.GetCommonRequesterProxierResponderFields(w2, false, true),
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (registerSynPacket.AtoEP) registerSynAckPacket.RequesterEndpoint = remoteEndpoint;

                    registerSynAckUdpPayload = registerSynAckPacket.EncodeAtResponder(null);
                    SendPacket(registerSynAckUdpPayload, remoteEndpoint);
                    WriteToLog_reg_responderSide_detail($"sent synAck");


                    var regAckScanner = RegisterAckPacket.GetScanner(null, registerSynPacket.RequesterPublicKey_RequestID, registerSynPacket.Timestamp32S);
                    RegisterAckPacket registerAckPacket;
                    if (registerSynPacket.AtoEP)
                    { // wait for reg ACK, retransmitting SynAck
                        WriteToLog_reg_responderSide_detail($"waiting for ack");
                        var regAckUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(registerSynAckUdpPayload, remoteEndpoint, regAckScanner);
                        WriteToLog_reg_responderSide_detail($"received ack");
                        registerAckPacket = RegisterAckPacket.DecodeAndVerifyAtResponder(regAckUdpPayload, registerSynPacket, registerSynAckPacket, newConnectionToNeighbor); // verifies hmac, decrypts endpoint of A
                    }
                    else
                    {// todo  retransmit until NHACK; at same time (!!!)  wait for regACK
                        throw new NotImplementedException(); 
                    }


                    WriteToLog_reg_responderSide_detail($"verified ack");
                    acceptAt.ConnectedPeers.Add(newConnectionToNeighbor); // added to list here in order to respond to ping requests from A                    
                    SendNextHopAckResponseToAck(registerAckPacket, remoteEndpoint); // send NHACK to ACK

                    _ = WaitForRegistrationConfirmationRequestAsync(remoteEndpoint, registerSynPacket, newConnectionToNeighbor);

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
                HandleExceptionInRegistrationResponder(remoteEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(registerSynPacket.RequesterPublicKey_RequestID);
            }
        }
        async Task WaitForRegistrationConfirmationRequestAsync(IPEndPoint remoteEndpoint, RegisterSynPacket syn, ConnectionToNeighbor newConnectionToNeighbor)
        {
            try
            {
                var regCfmScanner = RegisterConfirmationPacket.GetScanner(null, syn.RequesterPublicKey_RequestID, syn.Timestamp32S);
                WriteToLog_reg_responderSide_detail($"waiting for CFM");
                var regCfmUdpPayload = await OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(null, remoteEndpoint, regCfmScanner);
                WriteToLog_reg_responderSide_detail($"received CFM");
                var registerCfmPacket = RegisterConfirmationPacket.DecodeAndVerifyAtResponder(regCfmUdpPayload, syn, newConnectionToNeighbor);
                WriteToLog_reg_responderSide_detail($"verified CFM");

                SendNextHopAckResponseToCfm(registerCfmPacket, remoteEndpoint);
                WriteToLog_reg_responderSide_detail($"sent NHACK to CFM");
            }
			catch (Exception exc)
            {
                newConnectionToNeighbor.Dispose();
                HandleExceptionInRegistrationResponder(remoteEndpoint, exc);
            }
        }

        void SendNextHopAckResponseToSyn(RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint)
        {
            var nextHopAckPacket = new NextHopAckPacket
            {
                NhaSeq16 = registerSynPacket.NhaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (registerSynPacket.AtoEP == false)
            {
                //  nextHopAckPacket.SenderToken32 = x;
                //  nextHopAckPacket.SenderHMAC = x;
                throw new NotImplementedException();
            }
            var nextHopAckPacketData = nextHopAckPacket.Encode(registerSynPacket.AtoEP);
            
            RespondToRequestAndRetransmissions(registerSynPacket.OriginalUdpPayloadData, nextHopAckPacketData, remoteEndpoint);

         //   WriteToLog_reg_responderSide_detail($"sent nextHopAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(nextHopAckPacketData)} nhaSeq={registerSynPacket.NhaSeq16}");
        }

        void SendNextHopAckResponseToAck(RegisterAckPacket registerAckPacket, IPEndPoint remoteEndpoint)
        {
            var nextHopAckPacket = new NextHopAckPacket
            {
                NhaSeq16 = registerAckPacket.NhaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (registerAckPacket.AtoEP == false)
            {
                //  nextHopAckPacket.SenderToken32 = x;
                //  nextHopAckPacket.SenderHMAC = x;
                throw new NotImplementedException();
            }
            var nextHopAckPacketData = nextHopAckPacket.Encode(registerAckPacket.AtoEP);

            RespondToRequestAndRetransmissions(registerAckPacket.OriginalUdpPayloadData, nextHopAckPacketData, remoteEndpoint);
        }

        void SendNextHopAckResponseToCfm(RegisterConfirmationPacket registerCfmPacket, IPEndPoint remoteEndpoint)
        {
            var nextHopAckPacket = new NextHopAckPacket
            {
                NhaSeq16 = registerCfmPacket.NhaSeq16,
                StatusCode = NextHopResponseCode.accepted
            };
            if (registerCfmPacket.AtoEP == false)
            {
                //  nextHopAckPacket.SenderToken32 = x;
                //  nextHopAckPacket.SenderHMAC = x;
                throw new NotImplementedException();
            }
            var nextHopAckPacketData = nextHopAckPacket.Encode(registerCfmPacket.AtoEP);
            RespondToRequestAndRetransmissions(registerCfmPacket.OriginalUdpPayloadData, nextHopAckPacketData, remoteEndpoint);
        }

        bool ValidateReceivedTimestamp32S(uint receivedTimestamp32S)
        {
            var differenceS = Math.Abs((int)receivedTimestamp32S - Timestamp32S);
            return differenceS < Configuration.Timestamp32S_MaxDifferenceToAccept;
        }
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
