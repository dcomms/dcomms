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
            WriteToLog_reg_responderSide_detail(">> AcceptRegisterRequestAsync()", $"remoteEndpoint={remoteEndpoint}, NhaSeq16={registerSynPacket.NhaSeq16}, rpEndpoint={registerSynPacket.RpEndpoint}");
            if (_pendingRegisterRequests.Contains(registerSynPacket.RequesterPublicKey_RequestID))
            {
                SendNextHopAckResponseToSyn(registerSynPacket, remoteEndpoint);
                return; // it is a duplicate reg SYN request: NextHopAck got lost
            }
            _pendingRegisterRequests.Add(registerSynPacket.RequesterPublicKey_RequestID);
            try
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
                    throw new BadSignatureException();

                SendNextHopAckResponseToSyn(registerSynPacket, remoteEndpoint);

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer)
                {
                    LocalEndpoint = registerSynPacket.RpEndpoint,
					RemotePeerPublicKey = registerSynPacket.RequesterPublicKey_RequestID					
                };
                byte[] registerSynAckUdpPayload;
                try
                {
                    var registerSynAckPacket = new RegisterSynAckPacket
                    {                        
                        RequesterPublicKey_RequestID = registerSynPacket.RequesterPublicKey_RequestID,
                        RegisterSynTimestamp32S = registerSynPacket.Timestamp32S,
                        NeighborEcdhePublicKey = new EcdhPublicKey { ecdh25519PublicKey = newConnectionToNeighbor.LocalEcdhe25519PublicKey },
                        NeighborPublicKey = acceptAt.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                        NeighborStatusCode = DrpResponderStatusCode.confirmed,
                        NhaSeq16 = GetNewNhaSeq16(),
                    };
                    registerSynAckPacket.ToNeighborTxParametersEncrypted = newConnectionToNeighbor.EncryptAtRegisterResponder(registerSynPacket, registerSynAckPacket);
                    registerSynAckPacket.NeighborSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w2 => registerSynAckPacket.GetCommonRequesterAndResponderFields(w2, false, true),
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (registerSynPacket.AtoRP) registerSynAckPacket.RequesterEndpoint = remoteEndpoint;

                    registerSynAckUdpPayload = registerSynAckPacket.EncodeAtResponder(null);
                    SendPacket(registerSynAckUdpPayload, remoteEndpoint);
                    WriteToLog_reg_responderSide_detail("AcceptRegisterRequestAsync()", $"sent synAck");


                    var regAckScanner = RegisterAckPacket.GetScanner(null, registerSynPacket.RequesterPublicKey_RequestID, registerSynPacket.Timestamp32S);
                   // var regAckHeader = ms3.ToArray();
                    RegisterAckPacket registerAckPacket;
                    if (registerSynPacket.AtoRP)
                    { // wait for reg ACK
                        var regAckUdpPayload = await SendUdpRequestAsync_Retransmit_WaitForResponse(registerSynAckUdpPayload, remoteEndpoint, regAckScanner);

                        WriteToLog_reg_responderSide_detail("AcceptRegisterRequestAsync()", $"received regAck");
                        registerAckPacket = RegisterAckPacket.DecodeAndVerifyAtResponder(regAckUdpPayload, registerSynPacket, registerSynAckPacket, newConnectionToNeighbor); // verifies hmac, decrypts endpoint of A
                    }
                    else
                    {// todo  retransmit until NHA; at same time (!!!)  wait for regACK
                        throw new NotImplementedException(); // vanilla natural
                    }


                    WriteToLog_reg_responderSide_detail("AcceptRegisterRequestAsync()", $"verified regAck");
                    acceptAt.ConnectedPeers.Add(newConnectionToNeighbor); // added to list here in order to respond to ping requests from A
					
					// send ping
                    var pingRequestPacket = newConnectionToNeighbor.CreatePingRequestPacket(true);
                    
                    var pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PingResponsePacket.GetScanner(newConnectionToNeighbor.LocalRxToken32, pingRequestPacket.PingRequestId32), DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    pingRequestPacket.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    WriteToLog_reg_responderSide_detail("AcceptRegisterRequestAsync()", $"sent pingRequest");
                    var pingResponsePacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pingResponse from A
                    if (pingResponsePacketData == null) throw new DrpTimeoutException();
                    var pingResponsePacket = PingResponsePacket.DecodeAndVerify(_cryptoLibrary,
                        pingResponsePacketData, pingRequestPacket, newConnectionToNeighbor,
                        true, registerSynPacket, registerSynAckPacket);
                    WriteToLog_reg_responderSide_detail("AcceptRegisterRequestAsync()", $"verified pingResponse");
                }
                catch
                {
                    newConnectionToNeighbor.Dispose();
                    throw;
                }
            }
			catch (Exception exc)
            {
                HandleExceptionWhileConnectingToA(remoteEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(registerSynPacket.RequesterPublicKey_RequestID);
            }
        }

        void SendNextHopAckResponseToSyn(RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint)
        {
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
            var nextHopAckPacketData = nextHopAckPacket.Encode(registerSynPacket.AtoRP);
            SendPacket(nextHopAckPacketData, remoteEndpoint);
            WriteToLog_reg_responderSide_detail(null, $"sent nextHopAck to {remoteEndpoint}: {MiscProcedures.ByteArrayToString(nextHopAckPacketData)} nhaSeq={registerSynPacket.NhaSeq16}");
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
