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
                    LocalEndpoint = registerSynPacket.RpEndpoint
                };
                byte[] registerSynAckUdpPayload;
                try
                {
                    registerSynAckPacket.ToNeighborTxParametersEncrypted = EstablishedP2pStreamParameters.EncryptAtRegisterResponder(localEcdhe25519PrivateKey, registerSynPacket, registerSynAckPacket, newConnection.LocalRxToken32, _cryptoLibrary);
                    registerSynAckPacket.NeighborSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w2 => registerSynAckPacket.GetCommonRequesterAndResponderFields(w2, false, true),
                        acceptAt.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                    if (registerSynPacket.AtoRP) registerSynAckPacket.RequesterEndpoint = remoteEndpoint;

                    registerSynAckUdpPayload = registerSynAckPacket.EncodeAtResponder(null);
                    SendPacket(registerSynAckUdpPayload, remoteEndpoint);
												
                    //var pendingReqest = new PendingAcceptedRegisterRequest(this, registerSynPacket, registerSynAckPacket, registerSynAckUdpPayload, localEcdhe25519PrivateKey, requestReceivedAtUtc);
                    //pendingReqest.NewConnectionToRequester = newConnection;
                    //_pendingAcceptedRegisterRequests.Add(registerSynPacket.RequesterPublicKey_RequestID, pendingReqest);

                    PacketProcedures.CreateBinaryWriter(out var ms3, out var w3);
                    RegisterAckPacket.EncodeHeader(w3, null, registerSynPacket.RequesterPublicKey_RequestID, registerSynPacket.Timestamp32S);
                    var regAckHeader = ms3.ToArray();
                    if (registerSynPacket.AtoRP)
                    { // wait for reg ACK

                        var regAckUdpPayload = await SendUdpRequestAsync_Retransmit_WaitForResponse(registerSynAckUdpPayload, remoteEndpoint, regAckHeader);
                        var regAckPacket = new RegisterAckPacket(regAckUdpPayload);

                        //todo verify signature, decode endpoint of A
                        // when ready add newConnection to list
                    }
                    else
                    {//todo  retransmit until NHA; at same time (!!!)  wait for regACK
                        throw new NotImplementedException();
                    }
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
