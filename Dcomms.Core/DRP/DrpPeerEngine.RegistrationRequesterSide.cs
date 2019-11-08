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
        public void BeginRegister(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp, Action<LocalDrpPeer> cb = null)
        {
            WriteToLog_reg_requesterSide_detail($">> BeginRegister()", null, null);
            if (registrationConfiguration.LocalPeerRegistrationId == null) throw new ArgumentNullException();

            EngineThreadQueue.Enqueue(async () =>
            {               
                var r = await BeginRegister2(registrationConfiguration, drpPeerApp);
                cb?.Invoke(r);
            });            
        }

        public void BeginCreateLocalPeer(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp, Action<LocalDrpPeer> cb = null)
        {
            EngineThreadQueue.Enqueue(async () =>
            {
                var r = await CreateLocalPeerAsync(registrationConfiguration, drpPeerApp);
                if (cb != null) cb(r);
            });

        }

        async Task<LocalDrpPeer> CreateLocalPeerAsync(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp)
        {
            if (registrationConfiguration.LocalPeerRegistrationId == null) throw new ArgumentNullException();
            var localDrpPeer = new LocalDrpPeer(this, registrationConfiguration, drpPeerApp);

            if (Configuration.ForcedPublicIpApiProviderResponse == null)
            {
                WriteToLog_drpGeneral_detail($"resolving local public IP...");
                var sw = Stopwatch.StartNew();
                var localPublicIp = await SendPublicIpAddressApiRequestAsync("http://api.ipify.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://ip.seeip.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://bot.whatismyipaddress.com");
                if (localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");

                localDrpPeer.PublicIpApiProviderResponse = new IPAddress(localPublicIp);
                WriteToLog_drpGeneral_detail($"resolved local public IP = {localDrpPeer.PublicIpApiProviderResponse} ({(int)sw.Elapsed.TotalMilliseconds}ms)");
                await EngineThreadQueue.EnqueueAsync();
                WriteToLog_drpGeneral_detail($"@engine thread");
            }
            else
                localDrpPeer.PublicIpApiProviderResponse = Configuration.ForcedPublicIpApiProviderResponse;
                       

            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationId, localDrpPeer);
            return localDrpPeer;
        }
        async Task<LocalDrpPeer> BeginRegister2(LocalDrpPeerConfiguration registrationConfiguration, IDrpRegisteredPeerApp user)
        {
            WriteToLog_reg_requesterSide_detail($"@BeginRegister2() engine thread", null, null);

            var localDrpPeer = await CreateLocalPeerAsync(registrationConfiguration, user);
            if (registrationConfiguration.EntryPeerEndpoints.Length != 0)
            {
                foreach (var epEndpoint in registrationConfiguration.EntryPeerEndpoints) // try to connect to entry peers, one by one
                {
                   // if (MiscProcedures.EqualByteArrays(epEndpoint.Address.GetAddressBytes(), localDrpPeer.LocalPublicIpAddressForRegistration.GetAddressBytes()) == true)
                  //  {
                  //      logger.WriteToLog_detail($"not connecting to EP {epEndpoint}: same IP address as local public IP");
                  //  }
                   // else
                  //  {
                        try
                        {
                            if (await RegisterAsync(localDrpPeer, epEndpoint, 0, 20, null) == null)
                                continue;

                            //  on error or timeout try next entry server
                        }
                        catch (Exception exc)
                        {
                            HandleExceptionWhileConnectingToEP(epEndpoint, exc);
                        }
                  //  }
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
        public async Task<ConnectionToNeighbor> RegisterAsync(LocalDrpPeer localDrpPeer, IPEndPoint epEndpoint, uint minimalDistanceToNeighbor, byte numberofHops, double[] directionVectorNullable) // engine thread
        {
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
                    var registerPow1RequestPacket = GenerateRegisterPow1RequestPacket(localDrpPeer.PublicIpApiProviderResponse.GetAddressBytes(), Timestamp32S);

                    // send register pow1 request
                    WriteToLog_reg_requesterSide_detail($"PoW1 took {(int)pow1SW.Elapsed.TotalMilliseconds}ms. sending PoW1 request", null, null);
                    var rpPow1ResponsePacketData = await SendUdpRequestAsync_Retransmit(
                                new PendingLowLevelUdpRequest(epEndpoint,
                                    RegisterPow1ResponsePacket.GetScanner(registerPow1RequestPacket.Pow1RequestId),
                                    DateTimeNowUtc,
                                    Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                                    registerPow1RequestPacket.Encode(),
                                    Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS,
                                    Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                                ));
                    //  wait for response, retransmit
                    if (rpPow1ResponsePacketData == null) throw new DrpTimeoutException();
                    pow1ResponsePacket = new RegisterPow1ResponsePacket(rpPow1ResponsePacketData);
                    WriteToLog_reg_requesterSide_detail($"got PoW1 response with status={pow1ResponsePacket.StatusCode}", null, null);
                    if (pow1ResponsePacket.StatusCode != RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
                        throw new Pow1RejectedException(pow1ResponsePacket.StatusCode);
                }
                #endregion

                var newConnectionToNeighbor = new ConnectionToNeighbor(this, localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer, null);
             
                PongPacket pong;
                PendingLowLevelUdpRequest pendingPingRequest;
                var req = new RegisterRequestPacket
                {
                    RequesterRegistrationId = localDrpPeer.Configuration.LocalPeerRegistrationId,
                    ReqTimestamp64 = Timestamp64,
                    MinimalDistanceToNeighbor = minimalDistanceToNeighbor,
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
                    RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);

                    var pow2SW = Stopwatch.StartNew();
                    if (!Configuration.SandboxModeOnly_DisablePoW)
                    {
                        logger.WriteToLog_detail($"calculating PoW2");
                        GenerateRegisterReqPow2(req, pow1ResponsePacket.ProofOfWork2Request);
                    }
                    else
                        req.ProofOfWork2 = new byte[64];
                    pow2SW.Stop();

                    req.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                        w => req.GetSharedSignedFields(w, false),
                        localDrpPeer.Configuration.LocalPeerRegistrationPrivateKey
                        );
                    var reqToAck1Stopwatch = Stopwatch.StartNew();

                    logger.WriteToLog_detail($"PoW2 took {(int)pow2SW.Elapsed.TotalMilliseconds}ms. sending REQ, waiting for NPACK. ReqP2pSeq16={req.ReqP2pSeq16}");
                    #endregion

                  //  var reqSW = Stopwatch.StartNew();
                    #region wait for ACK1
                    var sentRequest = new SentRequest(this, logger, epEndpoint, null, req.Encode_OptionallySignNeighborHMAC(null), req.ReqP2pSeq16, RegisterAck1Packet.GetScanner(logger, req));
                    var ack1UdpData = await sentRequest.SendRequestAsync();

                    var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(logger, ack1UdpData, req, newConnectionToNeighbor);
                    logger.WriteToLog_detail($"verified ACK1. RequesterEndpoint={ack1.RequesterEndpoint}");
                    #endregion

                  

                    // check if it matches to previously known local public IP
                    if (ack1.RequesterEndpoint.Address.Equals(localDrpPeer.PublicIpApiProviderResponse) == false)
                    {
                        // MITM attack / EP sent local (requester) endpoint IP some bad IP address
                        throw new PossibleAttackException();
                    }
                    RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                    newConnectionToNeighbor.LocalEndpoint = ack1.RequesterEndpoint;
                    newConnectionToNeighbor.RemoteRegistrationId = ack1.ResponderRegistrationId;
                    reqToAck1Stopwatch.Stop();
                    var reqToAck1TimeMs = reqToAck1Stopwatch.Elapsed.TotalMilliseconds;
                    logger.WriteToLog_detail($"measured  REQ-ACK1_RTT={(int)reqToAck1TimeMs}ms");

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

                    logger.WriteToLog_detail($"sending ACK2 (in response to ACK1), waiting for NPACK");
                    RespondToRequestAndRetransmissions(ack1UdpData, ack2.Encode_OptionallySignNeighborHMAC(null), epEndpoint);
                    await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(null, epEndpoint, ack2.ReqP2pSeq16);
                    #endregion

                    var neighborWaitTimeMs = reqToAck1TimeMs * 0.5 - 250; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                    if (neighborWaitTimeMs > 20)
                    {
                        logger.WriteToLog_detail($"awaiting {(int)neighborWaitTimeMs}ms before PING...");
                        await EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the ACK2 reaches neighbor N via peers
                        logger.WriteToLog_detail($"... awaiting is complete");
                    }

                    localDrpPeer.AddToConnectedNeighbors(newConnectionToNeighbor, req);

                    #region send ping request directly to neighbor N, retransmit               
                    var pingRequest = newConnectionToNeighbor.CreatePing(true, false, localDrpPeer.ConnectedNeighborsBusySectorIds, localDrpPeer.AnotherNeighborToSameSectorExists(newConnectionToNeighbor));
                    pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                    PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, pingRequest.PingRequestId32),
                                    DateTimeNowUtc,
                                    Configuration.InitialPingRequests_ExpirationTimeoutS,
                                    pingRequest.Encode(),
                                    Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                    Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                                );

                    logger.WriteToLog_detail($"sending PING neighborToken32={pingRequest.NeighborToken32}, waiting for PONG");
                    var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest);
                    if (pongPacketData == null) throw new DrpTimeoutException();
                    if (newConnectionToNeighbor.IsDisposed) throw new DrpTimeoutException(); // ping timeout already destroyed the connection, so PONG response here is too late
                    pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
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
                    logger.WriteToLog_detail($"sending CFM, waiting for NPACK");
                    await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(cfm.Encode_OptionallySignNeighborHMAC(null), epEndpoint, cfm.ReqP2pSeq16);
                    logger.WriteToLog_detail($"received NPACK to CFM");
                }
                catch (Exception exc)
                {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                    logger.WriteToLog_mediumPain($"... registration confirmation request failed: {exc}");
                }
                #endregion

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
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                var response = await httpClient.GetAsync(url);
                var result = await response.Content.ReadAsStringAsync();
                var ipAddress = IPAddress.Parse(result);
                return ipAddress.GetAddressBytes();
            }
            catch (Exception exc)
            {
                HandleGeneralException($"public IP address API request to {url} failed", exc);
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
