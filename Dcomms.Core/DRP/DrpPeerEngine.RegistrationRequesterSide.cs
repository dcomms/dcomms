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
        public void BeginRegister(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp, Action<LocalDrpPeer> cb = null)
        {
            WriteToLog_reg_requesterSide_detail($">> BeginRegister()");
            if (registrationConfiguration.LocalPeerRegistrationId == null) throw new ArgumentNullException();

            EngineThreadQueue.Enqueue(async () =>
            {
                try
                {
                    var r = await BeginRegister2(registrationConfiguration, drpPeerApp);
                    if (cb != null) cb(r);
                }
                catch (Exception exc)
                {
                    HandleExceptionInRegistrationRequester(exc);
                }
            });
            
        }

        public void BeginCreateLocalPeer(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp, Action<LocalDrpPeer> cb = null)
        {
            EngineThreadQueue.Enqueue(async () =>
            {
                var r = await CreateLocalPeerAsync(registrationConfiguration, drpPeerApp);
                if (cb != null) cb(r);
            });

        }

        async Task<LocalDrpPeer> CreateLocalPeerAsync(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp)
        {
            if (registrationConfiguration.LocalPeerRegistrationId == null) throw new ArgumentNullException();
            var localDrpPeer = new LocalDrpPeer(this, registrationConfiguration, drpPeerApp);

            if (Configuration.ForcedPublicIpApiProviderResponse == null)
            {
                WriteToLog_drpGeneral_detail($"resolving local public IP...");
                var localPublicIp = await SendPublicIpAddressApiRequestAsync("http://api.ipify.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://ip.seeip.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://bot.whatismyipaddress.com");
                if (localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");

                localDrpPeer.PublicIpApiProviderResponse = new IPAddress(localPublicIp);
                WriteToLog_drpGeneral_detail($"resolved local public IP = {localDrpPeer.PublicIpApiProviderResponse}");
                await EngineThreadQueue.EnqueueAsync();
                WriteToLog_drpGeneral_detail($"@engine thread");
            }
            else
                localDrpPeer.PublicIpApiProviderResponse = Configuration.ForcedPublicIpApiProviderResponse;
                       

            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationId, localDrpPeer);
            return localDrpPeer;
        }
        async Task<LocalDrpPeer> BeginRegister2(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerApp user)
        {
            WriteToLog_reg_requesterSide_detail($"@BeginRegister2() engine thread");

            var localDrpPeer = await CreateLocalPeerAsync(registrationConfiguration, user);
            if (registrationConfiguration.EntryPeerEndpoints.Length != 0)
            {
                foreach (var epEndpoint in registrationConfiguration.EntryPeerEndpoints) // try to connect to entry peers, one by one
                {
                   // if (MiscProcedures.EqualByteArrays(epEndpoint.Address.GetAddressBytes(), localDrpPeer.LocalPublicIpAddressForRegistration.GetAddressBytes()) == true)
                  //  {
                  //      WriteToLog_reg_requesterSide_detail($"not connecting to EP {epEndpoint}: same IP address as local public IP");
                  //  }
                   // else
                  //  {
                        try
                        {
                            if (await RegisterAsync(localDrpPeer, epEndpoint) == null)
                                continue;

                            //  on error or timeout try next entry server
                        }
                        catch (Exception exc)
                        {
                            HandleExceptionWhileConnectingToRP(epEndpoint, exc);
                        }
                  //  }
                }

                WriteToLog_reg_requesterSide_detail($"@RegisterAsync() returned {localDrpPeer}");
                return localDrpPeer;
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <returns>null if registration failed with timeout or some error code</returns>
        public async Task<ConnectionToNeighbor> RegisterAsync(LocalDrpPeer localDrpPeer, IPEndPoint epEndpoint) // engine thread
        {
            WriteToLog_reg_requesterSide_detail($"connecting to EntryPeer {epEndpoint}");

            #region PoW1
            WriteToLog_reg_requesterSide_detail($"generating PoW1 request");
            var registerPow1RequestPacket = GenerateRegisterPow1RequestPacket(localDrpPeer.PublicIpApiProviderResponse.GetAddressBytes(), Timestamp32S);

            // send register pow1 request
            WriteToLog_reg_requesterSide_detail($"sending PoW1 request");
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
            var pow1ResponsePacket = new RegisterPow1ResponsePacket(rpPow1ResponsePacketData);
            WriteToLog_reg_requesterSide_detail($"got PoW1 response with status={pow1ResponsePacket.StatusCode}");
            if (pow1ResponsePacket.StatusCode != RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
                throw new Pow1RejectedException(pow1ResponsePacket.StatusCode);
            #endregion
                       
            var newConnectionToNeighbor = new ConnectionToNeighbor(this, localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer);
            RegisterRequestPacket req;
            PongPacket pong;
            PendingLowLevelUdpRequest pendingPingRequest;
            try
            {
                #region register REQ
                // calculate PoW2
                req = new RegisterRequestPacket
                {
                    RequesterRegistrationId = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId,
                    ReqTimestamp32S = Timestamp32S,
                    MinimalDistanceToNeighbor = 0,
                    NumberOfHopsRemaining = 10,
                    RequesterEcdhePublicKey = new EcdhPublicKey(newConnectionToNeighbor.LocalEcdhe25519PublicKey),
                    NpaSeq16 = GetNewNpaSeq16_AtoEP(),
                    EpEndpoint = epEndpoint
                };
                RecentUniquePublicEcdhKeys.AssertIsUnique(req.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                WriteToLog_reg_requesterSide_detail($"calculating PoW2");
                GenerateRegisterReqPow2(req, pow1ResponsePacket.ProofOfWork2Request);
                req.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => req.GetSharedSignedFields(w, false),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                    );
                var reqToAck1Stopwatch = Stopwatch.StartNew();

                WriteToLog_reg_requesterSide_detail($"sending REQ, waiting for NPACK. NpaSeq16={req.NpaSeq16}");
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(req.Encode_OptionallySignNeighborHMAC(null), epEndpoint, req.NpaSeq16);
                #endregion

                #region wait for ACK1
                WriteToLog_reg_requesterSide_detail($"waiting for ACK1");
                var ack1UdpData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(epEndpoint,
                                RegisterAck1Packet.GetScanner(req.RequesterRegistrationId, req.ReqTimestamp32S),
                                DateTimeNowUtc, Configuration.RegisterRequestsTimoutS                               
                            ));
                if (ack1UdpData == null) throw new DrpTimeoutException();
                var ack1 = RegisterAck1Packet.DecodeAndOptionallyVerify(ack1UdpData, req, newConnectionToNeighbor);
                WriteToLog_reg_requesterSide_detail($"verified ACK1. RequesterEndpoint={ack1.RequesterEndpoint}");
                #endregion

                // check if it matches to previously known local public IP
                if (ack1.RequesterEndpoint.Address.Equals(localDrpPeer.PublicIpApiProviderResponse) == false)
                {
                    // MITM attack / EP sent local (requester) endpoint IP some bad IP address
                    throw new PossibleMitmException();
                }
                RecentUniquePublicEcdhKeys.AssertIsUnique(ack1.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                newConnectionToNeighbor.LocalEndpoint = ack1.RequesterEndpoint;
                newConnectionToNeighbor.RemotePeerPublicKey = ack1.ResponderRegistrationId;
                reqToAck1Stopwatch.Stop();
                var reqToAck1TimeMs = reqToAck1Stopwatch.Elapsed.TotalMilliseconds;
                WriteToLog_reg_requesterSide_detail($"measured REQ-ACK1 RTT = {(int)reqToAck1TimeMs}ms");

                #region send ACK2, encode local IP
                var ack2 = new RegisterAck2Packet
                {
                    ReqTimestamp32S = req.ReqTimestamp32S,
                    RequesterRegistrationId = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId,
                    NpaSeq16 = GetNewNpaSeq16_AtoEP()
                };            
                ack2.ToRequesterTxParametersEncrypted = newConnectionToNeighbor.Encrypt_ack2_ToRequesterTxParametersEncrypted_AtRequester(req, ack1, ack2);
                newConnectionToNeighbor.InitializeP2pStream(req, ack1, ack2);
                ack2.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary, w =>
                    {
                        req.GetSharedSignedFields(w, true);
                        ack1.GetSharedSignedFields(w, true, true);
                        ack2.GetSharedSignedFields(w, false, true);
                    },
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                 );

                WriteToLog_reg_requesterSide_detail($"sending ACK2 (in response to ACK1), waiting for NPACK");
                RespondToRequestAndRetransmissions(ack1UdpData, ack2.Encode_OptionallySignNeighborHMAC(null), epEndpoint);
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(null, epEndpoint, ack2.NpaSeq16);
                #endregion

                var neighborWaitTimeMs = reqToAck1TimeMs * 0.5 - 100; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                if (neighborWaitTimeMs > 20)
                {
                    await EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the ACK2 reaches neighbor N via peers
                }
                                
                localDrpPeer.ConnectedNeighbors.Add(newConnectionToNeighbor);

                #region send ping request directly to neighbor N, retransmit               
                var pingRequest = newConnectionToNeighbor.CreatePing(true);
                pendingPingRequest = new PendingLowLevelUdpRequest(newConnectionToNeighbor.RemoteEndpoint,
                                PongPacket.GetScanner(newConnectionToNeighbor.LocalNeighborToken32, pingRequest.PingRequestId32),
                                DateTimeNowUtc,
                                Configuration.InitialPingRequests_ExpirationTimeoutS,
                                pingRequest.Encode(),
                                Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                            );

                WriteToLog_reg_requesterSide_detail($"sending PING, waiting for PONG");
                var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest);
                if (pongPacketData == null) throw new DrpTimeoutException();
                pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                    pongPacketData, pingRequest, newConnectionToNeighbor,
                    true);
                WriteToLog_reg_requesterSide_detail($"verified PONG");
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
                    ReqTimestamp32S = req.ReqTimestamp32S,
                    RequesterRegistrationId = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId,
                    ResponderRegistrationConfirmationSignature = pong.ResponderRegistrationConfirmationSignature,
                    NpaSeq16 = GetNewNpaSeq16_AtoEP()
                };
                cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => newConnectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                WriteToLog_reg_requesterSide_detail($"sending CFM, waiting for NPACK");
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(cfm.Encode_OptionallySignNeighborHMAC(null), epEndpoint, cfm.NpaSeq16);
                WriteToLog_reg_requesterSide_detail($"received NPACK to CFM");
            }
            catch (Exception exc)
            {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                WriteToLog_reg_requesterSide_mediumPain($"... registration confirmation request failed: {exc}");
            }
            #endregion
              
            return newConnectionToNeighbor;
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
                HandleGeneralException($"public IP address API request to {url} failed: {exc}");
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
