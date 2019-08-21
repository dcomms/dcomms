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
        async Task AcceptRegisterRequestAsync(LocalDrpPeer acceptAt, RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint, DateTime requestReceivedAtUtc) // engine thread
        {
            if (_pendingRegisterRequests.Contains(registerSynPacket.RequesterPublicKey_RequestID)) return; // it is a duplicate reg SYN request
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

                var newConnection = new ConnectedDrpPeer(this, acceptAt, ConnectedDrpPeerInitiatedBy.remotePeer)
                {
                    LocalEndpoint = registerSynPacket.RpEndpoint,
					RemotePeerPublicKey = registerSynPacket.RequesterPublicKey_RequestID					
                };
                byte[] registerSynAckUdpPayload;
                try
                {
                    registerSynAckPacket.ToNeighborTxParametersEncrypted = P2pStreamParameters.EncryptAtRegisterResponder(localEcdhe25519PrivateKey,
						registerSynPacket, registerSynAckPacket, newConnection.LocalRxToken32, _cryptoLibrary, out var sharedDhSecret);
                    registerSynAckPacket.NeighborSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w2 => registerSynAckPacket.GetCommonRequesterAndResponderFields(w2, false, true),
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (registerSynPacket.AtoRP) registerSynAckPacket.RequesterEndpoint = remoteEndpoint;

                    registerSynAckUdpPayload = registerSynAckPacket.EncodeAtResponder(null);
                    SendPacket(registerSynAckUdpPayload, remoteEndpoint);
												
                    
                    var regAckScanner = RegisterAckPacket.GetScanner(null, registerSynPacket.RequesterPublicKey_RequestID, registerSynPacket.Timestamp32S);
                   // var regAckHeader = ms3.ToArray();
                    RegisterAckPacket registerAckPacket;
                    if (registerSynPacket.AtoRP)
                    { // wait for reg ACK
                        var regAckUdpPayload = await SendUdpRequestAsync_Retransmit_WaitForResponse(registerSynAckUdpPayload, remoteEndpoint, regAckScanner);
                        registerAckPacket = RegisterAckPacket.DecodeAndVerifyAtResponder(_cryptoLibrary, regAckUdpPayload, sharedDhSecret, 
							registerSynPacket, registerSynAckPacket, out var txParameters);  // verifies hmac, decrypts endpoint of A
                        newConnection.TxParameters = txParameters;
                    }
                    else
                    {// todo  retransmit until NHA; at same time (!!!)  wait for regACK
                        throw new NotImplementedException(); // vanilla natural
                    }

                    acceptAt.ConnectedPeers.Add(newConnection); // added to list here in order to respond to ping requests from A
					
					// send ping
                    var pingRequestPacket = newConnection.CreatePingRequestPacket(true);
                    
                    var pendingPingRequest = new PendingLowLevelUdpRequest(newConnection.TxParameters.RemoteEndpoint,
                                    PingResponsePacket.GetScanner(newConnection.LocalRxToken32, pingRequestPacket.PingRequestId32), DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    pingRequestPacket.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );
                    var pingResponsePacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pingResponse from A
                    if (pingResponsePacketData == null) throw new DrpTimeoutException();
                    var pingResponsePacket = PingResponsePacket.DecodeAndVerify(_cryptoLibrary,
                        pingResponsePacketData, pingRequestPacket, newConnection,
                        true, registerSynPacket, registerSynAckPacket);                  
                }
                catch
                {
                    newConnection.Dispose();
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
