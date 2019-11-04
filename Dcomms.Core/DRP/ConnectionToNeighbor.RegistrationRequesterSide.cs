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
        internal async Task RegisterAsync(uint minimalDistanceToNeighbor, ushort busySectorIds, byte numberOfHopsRemaining, byte numberOfRandomHopsRemaining)
        {
            _engine.WriteToLog_reg_requesterSide_detail($">> ConnectionToNeighbor.RegisterAsync(minimalDistanceToNeighbor={minimalDistanceToNeighbor}", null, null);
            _localDrpPeer.CurrentRegistrationOperationsCount++;

            try
            {
                var newConnectionToNeighbor = new ConnectionToNeighbor(_engine, _localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer, null);                
                PongPacket pong;
                PendingLowLevelUdpRequest pendingPingRequest;
                var req = new RegisterRequestPacket
                {
                    RequesterRegistrationId = _localDrpPeer.Configuration.LocalPeerRegistrationId,
                    ReqTimestamp64 = _engine.Timestamp64,
                    MinimalDistanceToNeighbor = minimalDistanceToNeighbor,
                    RequesterNeighborsBusySectorIds = busySectorIds, 
                    NumberOfHopsRemaining = numberOfHopsRemaining,
                    NumberOfRandomHopsRemaining = numberOfRandomHopsRemaining,
                    RequesterEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                    ReqP2pSeq16 = GetNewRequestP2pSeq16_P2P(),
                    EpEndpoint = this.RemoteEndpoint
                };
                var logger = new Logger(Engine, LocalDrpPeer, req, DrpPeerEngine.VisionChannelModuleName_reg_requesterSide);
                try
                {  
                    _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

                    req.RequesterSignature = RegistrationSignature.Sign(_engine.CryptoLibrary,
                        w => req.GetSharedSignedFields(w, false),
                        _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                        );

                    var reqToAck1Stopwatch = Stopwatch.StartNew();
                    
                    #region wait for ACK1, respond with NPACK
                    logger.WriteToLog_detail($"sending {req}, waiting for NPACK. ReqP2pSeq16={req.ReqP2pSeq16}");

                    var sentRequest = new SentRequest(Engine, logger, this.RemoteEndpoint, this, req.Encode_OptionallySignNeighborHMAC(this),
                        req.ReqP2pSeq16, RegisterAck1Packet.GetScanner(logger, req, this));
                    var ack1UdpData = await sentRequest.SendRequestAsync();

                    var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(logger, ack1UdpData, req, newConnectionToNeighbor);
                    logger.WriteToLog_detail($"verified ACK1, sending NPACK to ACK1");

                    _engine.SendNeighborPeerAckResponseToRegisterAck1(ack1, this);
                    #endregion                 

                    if (newConnectionToNeighbor.IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {newConnectionToNeighbor} is disposed during reg. request 5345322345");
                        return;
                    }
                    if (IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {this} is disposed during reg. request 5345322345");
                        return;
                    }
                    _engine.RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                    newConnectionToNeighbor.LocalEndpoint = this.LocalEndpoint;
                    newConnectionToNeighbor.RemoteRegistrationId = ack1.ResponderRegistrationId;
                    reqToAck1Stopwatch.Stop();
                    var reqToAck1TimeMs = reqToAck1Stopwatch.Elapsed.TotalMilliseconds;
                    logger.WriteToLog_detail($"measured REQ-ACK1 RTT = {(int)reqToAck1TimeMs}ms");

                    #region send ACK2, encode local IP
                    var ack2 = new RegisterAck2Packet
                    {
                        ReqTimestamp64 = req.ReqTimestamp64,
                        RequesterRegistrationId = _localDrpPeer.Configuration.LocalPeerRegistrationId,
                        ReqP2pSeq16 = GetNewRequestP2pSeq16_P2P(),
                    };
                    ack2.ToRequesterTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack2_ToRequesterTxParametersEncrypted_AtRequester(logger, req, ack1, ack2);
                    newConnectionToNeighbor.InitializeP2pStream(req, ack1, ack2);
                    ack2.RequesterSignature = RegistrationSignature.Sign(_engine.CryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w, true);
                            ack1.GetSharedSignedFields(w, true, true);
                            ack2.GetSharedSignedFields(w, false, true);
                        },
                        _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                     );

                    logger.WriteToLog_detail($"sending ACK2 (in response to ACK1), waiting for NPACK");
                    await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(ack2.Encode_OptionallySignNeighborHMAC(this), this.RemoteEndpoint, ack2.ReqP2pSeq16);
                    #endregion

                    var neighborWaitTimeMs = reqToAck1TimeMs * 0.5 - 100; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                    if (neighborWaitTimeMs > 20)
                    {
                        await _engine.EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the ACK2 reaches neighbor N via peers
                    }


                    if (newConnectionToNeighbor.IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {newConnectionToNeighbor} is disposed during reg. request 234574568");
                        return;
                    }
                    if (IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {this} is disposed during reg. request 234574568");
                        return;
                    }

                    _localDrpPeer.AddToConnectedNeighbors(newConnectionToNeighbor, req);

                    #region send ping request directly to neighbor N, retransmit               
                    var pingRequest = newConnectionToNeighbor.CreatePing(true, false, _localDrpPeer.ConnectedNeighborsBusySectorIds);
                    pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, pingRequest.PingRequestId32),
                                    _engine.DateTimeNowUtc,
                                    _engine.Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    pingRequest.Encode(),
                                    _engine.Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    _engine.Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    logger.WriteToLog_detail($"sending PING, waiting for PONG");
                    var pongPacketData = await _engine.SendUdpRequestAsync_Retransmit(pendingPingRequest);
                    if (pongPacketData == null) throw new DrpTimeoutException();
                    if (newConnectionToNeighbor.IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {newConnectionToNeighbor} is disposed during reg. request 548798");
                        return;
                    }
                    if (IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {this} is disposed during reg. request 548798");
                        return;
                    }

                    pong = PongPacket.DecodeAndVerify(_engine.CryptoLibrary,
                        pongPacketData, pingRequest, newConnectionToNeighbor,
                        true);
                    logger.WriteToLog_detail($"verified PONG");
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
                    if (newConnectionToNeighbor.IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {newConnectionToNeighbor} is disposed during reg. request 541687987");
                        return;
                    }
                    if (IsDisposed)
                    {
                        logger.WriteToLog_needsAttention($"connection {this} is disposed during reg. request 541687987");
                        return;
                    }
                    var cfm = new RegisterConfirmationPacket
                    {
                        ReqTimestamp64 = req.ReqTimestamp64,
                        RequesterRegistrationId = _localDrpPeer.Configuration.LocalPeerRegistrationId,
                        ResponderRegistrationConfirmationSignature = pong.ResponderRegistrationConfirmationSignature,
                        ReqP2pSeq16 = GetNewRequestP2pSeq16_P2P()
                    };
                    cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Sign(_engine.CryptoLibrary,
                        w => newConnectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                        _localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey);
                    logger.WriteToLog_detail($"sending CFM, waiting for NPACK");
                    await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(cfm.Encode_OptionallySignNeighborHMAC(this), this.RemoteEndpoint, cfm.ReqP2pSeq16);
                    logger.WriteToLog_detail($"received NPACK to CFM");
                }
                catch (Exception exc)
                {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                    logger.WriteToLog_mediumPain($"... registration confirmation request failed: {exc}");
                }
                #endregion

                return;// newConnectionToNeighbor;
            }
            finally
            {
                _localDrpPeer.CurrentRegistrationOperationsCount--;
            }
        }
    }
}
