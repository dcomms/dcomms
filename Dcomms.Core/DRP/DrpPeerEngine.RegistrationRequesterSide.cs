using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    /// <summary>
    /// registration, for requester peer (A): connect to the p2P network via entry peer (EP)
    /// </summary>
    partial class DrpPeerEngine
    {
        /// <summary>
        /// returns control only when LocalDrpPeer is registered and ready for operation ("local user logged in")
        /// </summary>
        public void BeginRegister(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp, Action<LocalDrpPeer,Exception> cb = null)
        {
            WriteToLog_reg_requesterSide_detail($">> BeginRegister()", null, null);
            if (registrationConfiguration.LocalPeerRegistrationId == null) throw new ArgumentNullException();

            EngineThreadQueue.Enqueue(async () =>
            {
                LocalDrpPeer r = null;
                Exception exc = null;
                try
                {
                    r = await BeginRegister2(registrationConfiguration, drpPeerApp);
                }
                catch (Exception exc2)
                {
                    exc = exc2;
                }
                if (exc == null) WriteToLog_reg_requesterSide_detail($"<< BeginRegister() r={r}", null, null);
                else WriteToLog_reg_requesterSide_mediumPain($"<< BeginRegister() exc={exc}", null, null);
                cb?.Invoke(r, exc);
            }, "BeginRegister792");            
        }

        public void BeginCreateLocalPeer(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp, Action<LocalDrpPeer> cb = null)
        {
            EngineThreadQueue.Enqueue(() =>
            {
                var r = CreateLocalPeer(registrationConfiguration, drpPeerApp);
                cb?.Invoke(r);
            }, "CreateLocalPeerAsync24807");
        }

        LocalDrpPeer CreateLocalPeer(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp)
        {
            if (registrationConfiguration.LocalPeerRegistrationId == null) throw new ArgumentNullException();
            var localDrpPeer = new LocalDrpPeer(this, registrationConfiguration, drpPeerApp);
            localDrpPeer.PublicIpApiProviderResponse = LocalPublicIp;
            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationId, localDrpPeer);
            return localDrpPeer;
        }
        async Task<LocalDrpPeer> BeginRegister2(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp user)
        {
            WriteToLog_reg_requesterSide_detail($"@BeginRegister2() engine thread", null, null);

            var localDrpPeer = CreateLocalPeer(registrationConfiguration, user);
            if (registrationConfiguration.EntryPeerEndpoints.Length != 0)
            {
                for (; ;) // try forever
                {
                    var epIndex = _insecureRandom.Next(registrationConfiguration.EntryPeerEndpoints.Length);
                    var epEndpoint = registrationConfiguration.EntryPeerEndpoints[epIndex];
                                
                    try
                    {
                        if (await RegisterAsync(localDrpPeer, epEndpoint, 0, RegisterRequestPacket.MaxNumberOfHopsRemaining, null, true) != null)
                            break;

                        //  on error or timeout try next entry peer
                    }
                    catch (Exception exc)
                    {
                        HandleExceptionWhileConnectingToEP(epEndpoint, exc);
                    }              
                }

                WriteToLog_reg_requesterSide_detail($"@RegisterAsync() returned {localDrpPeer}", null, null);
                return localDrpPeer;
            }
            else
            {
                throw new ArgumentException();
            }
        }


        /// <returns>null if registration failed with timeout or some error code</returns>
        public async Task<ConnectionToNeighbor> RegisterAsync(LocalDrpPeer localDrpPeer, IPEndPoint epEndpoint, uint minimalDistanceToNeighbor,
            byte numberofHops, double[] directionVectorNullable, bool allowConnectionsToRequesterRegistrationId) // engine thread
        {
            var regSW = Stopwatch.StartNew();
            WriteToLog_reg_requesterSide_higherLevelDetail($"connecting via EntryPeer {epEndpoint}", null, null);
            localDrpPeer.CurrentRegistrationOperationsCount++;
            try
            {
                #region PoW1
                RegisterPow1ResponsePacket pow1ResponsePacket = null;
                if (!Configuration.SandboxModeOnly_DisablePoW)
                {
                    WriteToLog_reg_requesterSide_detail($"generating PoW1 request", null,  null);
                    var pow1SW = Stopwatch.StartNew();

                    await PowThreadQueue.EnqueueAsync("pow1 6318");
                    WriteToLog_reg_requesterSide_detail($"generating PoW1 request @pow thread", null, null);                  
                    var registerPow1RequestPacket = GenerateRegisterPow1RequestPacket(localDrpPeer.PublicIpApiProviderResponse.GetAddressBytes(), Timestamp32S);
                    await EngineThreadQueue.EnqueueAsync("pow1 234709");
                    WriteToLog_reg_requesterSide_detail($"generated PoW1 request @engine thread", null, null);

                    // send register pow1 request
                    if (pow1SW.Elapsed.TotalMilliseconds > 3000) WriteToLog_reg_requesterSide_lightPain($"PoW1 took {(int)pow1SW.Elapsed.TotalMilliseconds}ms", null, null);
                    WriteToLog_reg_requesterSide_detail($"PoW1 took {(int)pow1SW.Elapsed.TotalMilliseconds}ms. sending PoW1 request", null, null);
                    var rpPow1ResponsePacketData = await SendUdpRequestAsync_Retransmit(
                                new PendingLowLevelUdpRequest("rpPow1 469",  epEndpoint,
                                    RegisterPow1ResponsePacket.GetScanner(registerPow1RequestPacket.Pow1RequestId),
                                    PreciseDateTimeNowUtc,
                                    Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                                    registerPow1RequestPacket.Encode(),
                                    Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS,
                                    Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                                ));
                    //  wait for response, retransmit
                    if (rpPow1ResponsePacketData == null) throw new DrpTimeoutException($"pow1 request to EP '{epEndpoint}' (timeout={Configuration.UdpLowLevelRequests_ExpirationTimeoutS}s)"); ;
                    pow1ResponsePacket = new RegisterPow1ResponsePacket(rpPow1ResponsePacketData);
                    WriteToLog_reg_requesterSide_detail($"got PoW1 response with status={pow1ResponsePacket.StatusCode}", null, null);
                    if (pow1ResponsePacket.StatusCode != RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
                        throw new Pow1RejectedException(pow1ResponsePacket.StatusCode);
                }
                #endregion

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer, null);
             
                PongPacket pong;             
                var req = new RegisterRequestPacket
                {
                    RequesterRegistrationId = localDrpPeer.Configuration.LocalPeerRegistrationId,
                    ReqTimestamp64 = Timestamp64,
                    MinimalDistanceToNeighbor = minimalDistanceToNeighbor,
                    RequesterNatBehaviour = LocalNatBehaviour,
                    AllowConnectionsToRequesterRegistrationId = allowConnectionsToRequesterRegistrationId,
                    NumberOfHopsRemaining = numberofHops,
                    RequesterEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                    ReqP2pSeq16 = GetNewNpaSeq16_AtoEP(),
                    EpEndpoint = epEndpoint,
                    DirectionVectorNullableD = directionVectorNullable
                };
                var logger = new Logger(this, localDrpPeer, req, VisionChannelModuleName_reg_requesterSide);
                try
                {
                    #region register REQ  PoW2  
                    RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey, $"req.RequesterEcdhePublicKey {req}"); // this is used in non-standard way when routing registration requests to same (local) reg. ID,  too

                    var pow2SW = Stopwatch.StartNew();
                    if (!Configuration.SandboxModeOnly_DisablePoW)
                    {
                        await PowThreadQueue.EnqueueAsync("pow2 23465");
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"calculating PoW2 @pow thread");
                        GenerateRegisterReqPow2(req, pow1ResponsePacket.ProofOfWork2Request);
                        await EngineThreadQueue.EnqueueAsync("pow2 2496");
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"calculated PoW2 @engine thread");
                    }
                    else
                        req.ProofOfWork2 = new byte[64];
                    pow2SW.Stop();
                    if (pow2SW.Elapsed.TotalMilliseconds > 3000) logger.WriteToLog_lightPain($"PoW2 took {(int)pow2SW.Elapsed.TotalMilliseconds}ms");

                    req.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w => req.GetSharedSignedFields(w, false),
                        localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                        );
                    var reqToAck1Stopwatch = Stopwatch.StartNew();

                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"PoW2 took {(int)pow2SW.Elapsed.TotalMilliseconds}ms. sending REQ, waiting for NPACK. ReqP2pSeq16={req.ReqP2pSeq16}");
                    #endregion

                  //  var reqSW = Stopwatch.StartNew();
                    #region wait for ACK1
                    var sentRequest = new SentRequest(this, logger, epEndpoint, null, req.Encode_OptionallySignNeighborHMAC(null), req.ReqP2pSeq16, RegisterAck1Packet.GetScanner(logger, req));
                    var ack1UdpData = await sentRequest.SendRequestAsync("reg req ack1 367097");

                    var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(logger, ack1UdpData, req, newConnectionToNeighbor);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified ACK1. RequesterEndpoint={ack1.RequesterEndpoint}");
                    #endregion

                  

                    // check if it matches to previously known local public IP
                    if (ack1.RequesterEndpoint.Address.Equals(localDrpPeer.PublicIpApiProviderResponse) == false)
                    {
                        // MITM attack / EP sent local (requester) endpoint IP some bad IP address
                        throw new PossibleAttackException();
                    }
                    RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey, $"ack1.ResponderEcdhePublicKey from {epEndpoint}");

                    newConnectionToNeighbor.LocalEndpoint = ack1.RequesterEndpoint;
                    newConnectionToNeighbor.RemoteRegistrationId = ack1.ResponderRegistrationId;
                    reqToAck1Stopwatch.Stop();
                    var reqToAck1TimeMs = reqToAck1Stopwatch.Elapsed.TotalMilliseconds;
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"measured  REQ-ACK1_RTT={(int)reqToAck1TimeMs}ms");

                    #region send ACK2, encode local IP
                    var ack2 = new RegisterAck2Packet
                    {
                        ReqTimestamp64 = req.ReqTimestamp64,
                        RequesterRegistrationId = localDrpPeer.Configuration.LocalPeerRegistrationId,
                        ReqP2pSeq16 = GetNewNpaSeq16_AtoEP()
                    };
                    ack2.ToRequesterTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack2_ToRequesterTxParametersEncrypted_AtRequester(logger, req, ack1, ack2);
                    newConnectionToNeighbor.InitializeP2pStream(req, ack1, ack2);
                    ack2.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary, w =>
                        {
                            req.GetSharedSignedFields(w, true);
                            ack1.GetSharedSignedFields(w, true, true);
                            ack2.GetSharedSignedFields(w, false, true);
                        },
                        localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                     );

                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending ACK2 (in response to ACK1), waiting for NPACK");
                    RespondToRequestAndRetransmissions(ack1UdpData, ack2.Encode_OptionallySignNeighborHMAC(null), epEndpoint);
                    await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("ack2 46873", null, epEndpoint, ack2.ReqP2pSeq16);
                    #endregion

                    var neighborWaitTimeMs = reqToAck1TimeMs * 0.5 - 250; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                    if (neighborWaitTimeMs > 20)
                    {
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"awaiting {(int)neighborWaitTimeMs}ms before PING...");
                        await EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs), "before PING 34589"); // wait until the ACK2 reaches neighbor N via peers
                        if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"... awaiting is complete");
                    }

                    localDrpPeer.AddToConnectedNeighbors(newConnectionToNeighbor, req);

                    #region send ping request directly to neighbor N, retransmit               
                    var pingRequest = newConnectionToNeighbor.CreatePing(true, false, localDrpPeer.ConnectedNeighborsBusySectorIds, localDrpPeer.AnotherNeighborToSameSectorExists(newConnectionToNeighbor));
                    newConnectionToNeighbor.InitialPendingPingRequest = new PendingLowLevelUdpRequest("pingRequest 3850", newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, pingRequest.PingRequestId32),
                                    PreciseDateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    pingRequest.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending PING neighborToken32={pingRequest.NeighborToken32}, waiting for PONG");
                    var pongPacketData = await SendUdpRequestAsync_Retransmit(newConnectionToNeighbor.InitialPendingPingRequest);
                    if (pongPacketData == null) throw new DrpTimeoutException($"initial reg. requester PING to {newConnectionToNeighbor} (timeout={Configuration.InitialPingRequests_ExpirationTimeoutS}s)");
                    if (newConnectionToNeighbor.IsDisposed) throw new ObjectDisposedException($"initial reg. requester PING to {newConnectionToNeighbor} (special case: connection is disposed)", (Exception)null); // ping timeout already destroyed the connection, so PONG response here is too late
                    pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                        pongPacketData, pingRequest, newConnectionToNeighbor,
                        true);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"verified PONG");
                    newConnectionToNeighbor.OnReceivedVerifiedPong(pong, newConnectionToNeighbor.InitialPendingPingRequest.ResponseReceivedAtUtc.Value,
                        newConnectionToNeighbor.InitialPendingPingRequest.ResponseReceivedAtUtc.Value - newConnectionToNeighbor.InitialPendingPingRequest.InitialTxTimeUTC.Value);
                    #endregion
                }
                catch
                {
                    // todo update QoS
                    newConnectionToNeighbor.Dispose(); // remove from token32 table
                    throw;
                }

                #region send registration confirmation packet to EP->X->N
                try
                {
                    var cfm = new RegisterConfirmationPacket
                    {
                        ReqTimestamp64 = req.ReqTimestamp64,
                        RequesterRegistrationId = localDrpPeer.Configuration.LocalPeerRegistrationId,
                        ResponderRegistrationConfirmationSignature = pong.ResponderRegistrationConfirmationSignature,
                        ReqP2pSeq16 = GetNewNpaSeq16_AtoEP()
                    };
                    cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w => newConnectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                        localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"sending CFM, waiting for NPACK");
                    await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("cfm 32107", cfm.Encode_OptionallySignNeighborHMAC(null), epEndpoint, cfm.ReqP2pSeq16);
                    if (logger.WriteToLog_detail_enabled) logger.WriteToLog_detail($"received NPACK to CFM");
                }
                catch (DrpTimeoutException exc)
                {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                    logger.WriteToLog_needsAttention($"... registration confirmation request failed: {exc}");
                }
                catch (Exception exc)
                {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                    logger.WriteToLog_mediumPain($"... registration confirmation request failed: {exc}");
                }
                #endregion

                regSW.Stop();
                if (regSW.Elapsed.TotalMilliseconds > 5000) logger.WriteToLog_lightPain($"registration is completed in {(int)regSW.Elapsed.TotalMilliseconds}ms");
                else logger.WriteToLog_higherLevelDetail($"registration is completed in {(int)regSW.Elapsed.TotalMilliseconds}ms"); ;

                return newConnectionToNeighbor;
            }
            finally
            {
                localDrpPeer.CurrentRegistrationOperationsCount--;
            }
        }
        /// <returns>bytes of IP address</returns>
        async Task<byte[]> SendPublicIpAddressApiRequestAsync(string url)
        {
            WriteToLog_drpGeneral_detail($">> SendPublicIpAddressApiRequestAsync({url})");
            string result = "";
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                var response = await httpClient.GetAsync(url);
                result = await response.Content.ReadAsStringAsync();
                result = result.Trim();
                var ipAddress = IPAddress.Parse(result);
                WriteToLog_drpGeneral_detail($"<< SendPublicIpAddressApiRequestAsync({url}) returns {ipAddress}");
                return ipAddress.GetAddressBytes();
            }
            catch (Exception exc)
            {
                WriteToLog_drpGeneral_needsAttention($"public IP address API request to {url} failed. result: '{result}': {exc.Message}");
                return null;
            }
        }
        /// <summary>
        /// performs PoW#1 (stateless proof of work)
        /// </summary>
        RegisterPow1RequestPacket GenerateRegisterPow1RequestPacket(byte[] clientPublicIp, uint timeSec32UTC)
        {
            var packet = new RegisterPow1RequestPacket();
            packet.Timestamp32S = timeSec32UTC;
            packet.ProofOfWork1 = new byte[64];

            var rnd = new Random(_insecureRandom.Next());
            for (; ; )
            {
                rnd.NextBytes(packet.ProofOfWork1);
                if (Pow1IsOK(packet, clientPublicIp)) break;
            }

            packet.Pow1RequestId = (uint)rnd.Next();
            return packet;
        }
        void GenerateRegisterReqPow2(RegisterRequestPacket packet, byte[] proofOfWork2Request)
        {
            packet.ProofOfWork2 = new byte[64];
            var rnd = new Random();
            for (; ; )
            {
                rnd.NextBytes(packet.ProofOfWork2);
                if (Pow2IsOK(packet, proofOfWork2Request)) break;
            }
        }
    }
}
