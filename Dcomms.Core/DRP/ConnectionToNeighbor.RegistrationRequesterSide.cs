using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class ConnectionToNeighbor
    {
        /// <summary>
        /// is used to expand neighborhood
        /// </summary>
        internal async Task<ConnectionToNeighbor> RegisterAsync(uint minimalDistanceToNeighbor)
        {
            _engine.WriteToLog_reg_requesterSide_detail($">> ConnectionToNeighbor.RegisterAsync(minimalDistanceToNeighbor={minimalDistanceToNeighbor}");
            _localDrpPeer.CurrentRegistrationOperationsCount++;

            try
            {
                var newConnectionToNeighbor = new ConnectionToNeighbor(_engine, _localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer);
                RegisterRequestPacket req;
                PongPacket pong;
                PendingLowLevelUdpRequest pendingPingRequest;
                try
                {
                    #region register REQ
                    // calculate PoW2
                    req = new RegisterRequestPacket
                    {
                        RequesterRegistrationId = _localDrpPeer.Configuration.LocalPeerRegistrationId,
                        ReqTimestamp64 = _engine.Timestamp64,
                        MinimalDistanceToNeighbor = minimalDistanceToNeighbor,
                        NumberOfHopsRemaining = 10,
                        RequesterEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                        NpaSeq16 = GetNewNpaSeq16_P2P(),
                        EpEndpoint = this.RemoteEndpoint
                    };
                    _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

                    req.RequesterSignature = RegistrationSignature.Sign(_engine.CryptoLibrary,
                        w => req.GetSharedSignedFields(w, false),
                        _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                        );
                    var reqToAck1Stopwatch = Stopwatch.StartNew();

                    _engine.WriteToLog_reg_requesterSide_detail($"sending REQ, waiting for NPACK. NpaSeq16={req.NpaSeq16}");
                    await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(req.Encode_OptionallySignNeighborHMAC(this), this.RemoteEndpoint, req.NpaSeq16);
                    #endregion

                    #region wait for ACK1, respond with NPACK
                    _engine.WriteToLog_reg_requesterSide_detail($"waiting for ACK1");
                    var ack1UdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(this.RemoteEndpoint,
                                    RegisterAck1Packet.GetScanner(req.RequesterRegistrationId, req.ReqTimestamp64, this),
                                    _engine.DateTimeNowUtc, _engine.Configuration.RegisterRequestsTimoutS
                                ));
                    if (ack1UdpData == null) throw new DrpTimeoutException();
                    var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(ack1UdpData, req, newConnectionToNeighbor);
                    _engine.WriteToLog_reg_requesterSide_detail($"verified ACK1, sending NPACK to ACK1");

                    _engine.SendNeighborPeerAckResponseToRegisterAck1(ack1, this);
                    #endregion

                    if (ack1.ResponderStatusCode != DrpResponderStatusCode.confirmed)
                    {
                        _engine.WriteToLog_reg_requesterSide_lightPain($"got ACK1 with error={ack1.ResponderStatusCode}");
                        throw new DrpResponderRejectedException(ack1.ResponderStatusCode);
                    }

                    _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                    newConnectionToNeighbor.LocalEndpoint = this.LocalEndpoint;
                    newConnectionToNeighbor.RemoteRegistrationId = ack1.ResponderRegistrationId;
                    reqToAck1Stopwatch.Stop();
                    var reqToAck1TimeMs = reqToAck1Stopwatch.Elapsed.TotalMilliseconds;
                    _engine.WriteToLog_reg_requesterSide_detail($"measured REQ-ACK1 RTT = {(int)reqToAck1TimeMs}ms");

                    #region send ACK2, encode local IP
                    var ack2 = new RegisterAck2Packet
                    {
                        ReqTimestamp64 = req.ReqTimestamp64,
                        RequesterRegistrationId = _localDrpPeer.Configuration.LocalPeerRegistrationId,
                        NpaSeq16 = GetNewNpaSeq16_P2P(),
                    };
                    ack2.ToRequesterTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack2_ToRequesterTxParametersEncrypted_AtRequester(req, ack1, ack2);
                    newConnectionToNeighbor.InitializeP2pStream(req, ack1, ack2);
                    ack2.RequesterSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w, true);
                            ack1.GetSharedSignedFields(w, true, true);
                            ack2.GetSharedSignedFields(w, false, true);
                        },
                        _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                     );

                    _engine.WriteToLog_reg_requesterSide_detail($"sending ACK2 (in response to ACK1), waiting for NPACK");
                    await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack2.Encode_OptionallySignNeighborHMAC(this), this.RemoteEndpoint, ack2.NpaSeq16);
                    #endregion

                    var neighborWaitTimeMs = reqToAck1TimeMs * 0.5 - 100; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                    if (neighborWaitTimeMs > 20)
                    {
                        await _engine.EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the ACK2 reaches neighbor N via peers
                    }

                    _localDrpPeer.ConnectedNeighbors.Add(newConnectionToNeighbor);

                    #region send ping request directly to neighbor N, retransmit               
                    var pingRequest = newConnectionToNeighbor.CreatePing(true);
                    pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, pingRequest.PingRequestId32),
                                    _engine.DateTimeNowUtc,
                                    _engine.Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    pingRequest.Encode(),
                                    _engine.Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    _engine.Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    _engine.WriteToLog_reg_requesterSide_detail($"sending PING, waiting for PONG");
                    var pongPacketData = await _engine.SendUdpRequestAsync_Retransmit(pendingPingRequest);
                    if (pongPacketData == null) throw new DrpTimeoutException();
                    pong = PongPacket.DecodeAndVerify(_engine.CryptoLibrary,
                        pongPacketData, pingRequest, newConnectionToNeighbor,
                        true);
                    _engine.WriteToLog_reg_requesterSide_detail($"verified PONG");
                    newConnectionToNeighbor.OnReceivedVerifiedPong(pong, pendingPingRequest.ResponseReceivedAtUtc.Value,
                        pendingPingRequest.ResponseReceivedAtUtc.Value - pendingPingRequest.InitialTxTimeUTC.Value);
                    #endregion
                }
                catch
                {
                    // todo update QoS
                    newConnectionToNeighbor.Dispose(); // remove from token32 table
                    throw;
                }

                #region send registration confirmation packet to X->N
                try
                {
                    var cfm = new RegisterConfirmationPacket
                    {
                        ReqTimestamp64 = req.ReqTimestamp64,
                        RequesterRegistrationId = _localDrpPeer.Configuration.LocalPeerRegistrationId,
                        ResponderRegistrationConfirmationSignature = pong.ResponderRegistrationConfirmationSignature,
                        NpaSeq16 = GetNewNpaSeq16_P2P()
                    };
                    cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary,
                        w => newConnectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                        _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey);
                    _engine.WriteToLog_reg_requesterSide_detail($"sending CFM, waiting for NPACK");
                    await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(cfm.Encode_OptionallySignNeighborHMAC(this), this.RemoteEndpoint, cfm.NpaSeq16);
                    _engine.WriteToLog_reg_requesterSide_detail($"received NPACK to CFM");
                }
                catch (Exception exc)
                {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                    _engine.WriteToLog_reg_requesterSide_mediumPain($"... registration confirmation request failed: {exc}");
                }
                #endregion

                return newConnectionToNeighbor;
            }
            finally
            {
                _localDrpPeer.CurrentRegistrationOperationsCount--;
            }
        }
    }
}
