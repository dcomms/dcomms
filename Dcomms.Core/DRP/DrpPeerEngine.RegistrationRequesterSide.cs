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
        public void BeginRegister(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user, Action<LocalDrpPeer> cb = null)
        {
            WriteToLog_reg_requesterSide_detail($">> BeginRegister()");
                       
            EngineThreadQueue.Enqueue(async () =>
            {
                var r = await BeginRegister2(registrationConfiguration, user);
                if (cb != null) cb(r);
            });
            
        }

        public void BeginCreateLocalPeer(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user, Action<LocalDrpPeer> cb = null)
        {
            EngineThreadQueue.Enqueue(async () =>
            {
                var r = await CreateLocalPeerAsync(registrationConfiguration, user);
                if (cb != null) cb(r);
            });

        }

        async Task<LocalDrpPeer> CreateLocalPeerAsync(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            var localDrpPeer = new LocalDrpPeer(this, registrationConfiguration, user);

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
                       

            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationPublicKey, localDrpPeer);
            return localDrpPeer;
        }
        async Task<LocalDrpPeer> BeginRegister2(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
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
            WriteToLog_reg_requesterSide_detail($"connecting to EP {epEndpoint}");

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
                       
            var connectionToNeighbor = new ConnectionToNeighbor(this, localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer);
            RegisterRequestPacket syn;
            PongPacket pong;
            PendingLowLevelUdpRequest pendingPingRequest;
            try
            {
                #region register REQ
                // calculate PoW2
                syn = new RegisterRequestPacket
                {
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    Timestamp32S = Timestamp32S,
                    MinimalDistanceToNeighbor = 0,
                    NumberOfHopsRemaining = 10,
                    RequesterEcdhePublicKey = new EcdhPublicKey(connectionToNeighbor.LocalEcdhe25519PublicKey),
                    NpaSeq16 = GetNewNhaSeq16_AtoEP(),
                    EpEndpoint = epEndpoint
                };
                RecentUniquePublicEcdhKeys.AssertIsUnique(syn.RequesterEcdhePublicKey.Ecdh25519PublicKey);
                WriteToLog_reg_requesterSide_detail($"calculating PoW2");
                GenerateRegisterSynPow2(syn, pow1ResponsePacket.ProofOfWork2Request);
                syn.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => syn.GetCommonRequesterProxyResponderFields(w, false),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                    );
                var synToSynAckStopwatch = Stopwatch.StartNew();

                WriteToLog_reg_requesterSide_detail($"sending REQ, waiting for NPACK. NpaSeq16={syn.NpaSeq16}");
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(syn.Encode_OptionallySignSenderHMAC(null), epEndpoint, syn.NpaSeq16);
                #endregion

                #region wait for SYNACK
                WriteToLog_reg_requesterSide_detail($"waiting for SYNACK");
                var registerSynAckPacketData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(epEndpoint,
                                RegisterAck1Packet.GetScanner(syn.RequesterPublicKey_RequestID, syn.Timestamp32S),
                                DateTimeNowUtc, Configuration.RegSynAckTimoutS                               
                            ));
                if (registerSynAckPacketData == null) throw new DrpTimeoutException();
                var synAck = RegisterAck1Packet.DecodeAndOptionallyVerify(registerSynAckPacketData, syn, connectionToNeighbor);
                WriteToLog_reg_requesterSide_detail($"verified SYNACK. RequesterEndpoint={synAck.RequesterEndpoint}");
                #endregion

                // check if it matches to previously known local public IP
                if (synAck.RequesterEndpoint.Address.Equals(localDrpPeer.PublicIpApiProviderResponse) == false)
                {
                    // MITM attack / EP sent local (requester) endpoint IP some bad IP address
                    throw new PossibleMitmException();
                }
                RecentUniquePublicEcdhKeys.AssertIsUnique(synAck.ResponderEcdhePublicKey.Ecdh25519PublicKey);

                connectionToNeighbor.LocalEndpoint = synAck.RequesterEndpoint;
                connectionToNeighbor.RemotePeerPublicKey = synAck.ResponderPublicKey;
                synToSynAckStopwatch.Stop();
                var synToSynAckTimeMs = synToSynAckStopwatch.Elapsed.TotalMilliseconds;
                WriteToLog_reg_requesterSide_detail($"measured REQ-SYNACK RTT = {(int)synToSynAckTimeMs}ms");

                #region send ACK, encode local IP
                var ack = new RegisterAck2Packet
                {
                    RegisterSynTimestamp32S = syn.Timestamp32S,
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    NpaSeq16 = GetNewNhaSeq16_AtoEP()
                };            
                ack.ToRequesterTxParametersEncrypted = connectionToNeighbor.Encrypt_ack_ToRequesterTxParametersEncrypted_AtRequester(syn, synAck, ack);
                connectionToNeighbor.InitializeP2pStream(syn, synAck, ack);
                ack.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary, w =>
                    {
                        syn.GetCommonRequesterProxyResponderFields(w, true);
                        synAck.GetCommonRequesterProxierResponderFields(w, true, true);
                        ack.GetCommonRequesterProxyResponderFields(w, false, true);
                    },
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                 );

                WriteToLog_reg_requesterSide_detail($"sending ACK, waiting for NPACK");
                RespondToRequestAndRetransmissions(registerSynAckPacketData, ack.Encode_OptionallySignSenderHMAC(null), epEndpoint);
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(null, epEndpoint, ack.NpaSeq16);
                #endregion

                var neighborWaitTimeMs = synToSynAckTimeMs * 0.5 - 100; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                if (neighborWaitTimeMs > 20)
                {
                    await EngineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the registerACK reaches neighbor N via peers
                }
                                
                localDrpPeer.ConnectedPeers.Add(connectionToNeighbor);

                #region send ping request directly to neighbor N, retransmit               
                var pingRequest = connectionToNeighbor.CreatePing(true);
                pendingPingRequest = new PendingLowLevelUdpRequest(connectionToNeighbor.RemoteEndpoint,
                                PongPacket.GetScanner(connectionToNeighbor.LocalRxToken32, pingRequest.PingRequestId32),
                                DateTimeNowUtc,
                                Configuration.InitialPingRequests_ExpirationTimeoutS,
                                pingRequest.Encode(),
                                Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                            );

                WriteToLog_reg_requesterSide_detail($"sending ping, waiting for pong");
                var pongPacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest);
                if (pongPacketData == null) throw new DrpTimeoutException();
                pong = PongPacket.DecodeAndVerify(_cryptoLibrary,
                    pongPacketData, pingRequest, connectionToNeighbor,
                    true);
                WriteToLog_reg_requesterSide_detail($"verified pong");
                connectionToNeighbor.OnReceivedVerifiedPong(pong, pendingPingRequest.ResponseReceivedAtUtc.Value,
                    pendingPingRequest.ResponseReceivedAtUtc.Value - pendingPingRequest.InitialTxTimeUTC.Value);
                #endregion
            }
            catch
            {
                // todo update quality
                connectionToNeighbor.Dispose(); // remove from token32 table
                throw;
            }
                       
            #region send registration confirmation packet to EP->X->N
            try
            {
                var cfm = new RegisterConfirmationPacket
                {
                    RegisterSynTimestamp32S = syn.Timestamp32S,
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    ResponderRegistrationConfirmationSignature = pong.ResponderRegistrationConfirmationSignature,
                    NpaSeq16 = GetNewNhaSeq16_AtoEP()
                };
                cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => connectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                WriteToLog_reg_requesterSide_detail($"sending CFM, waiting for NPACK");
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(cfm.Encode_OptionallySignSenderHMAC(null), epEndpoint, cfm.NpaSeq16);
                WriteToLog_reg_requesterSide_detail($"received NPACK to CFM");
            }
            catch (Exception exc)
            {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                WriteToLog_reg_requesterSide_mediumPain($"... registration confirmation request failed: {exc}");
            }
            #endregion
              
            return connectionToNeighbor;
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
        void GenerateRegisterSynPow2(RegisterRequestPacket packet, byte[] proofOfWork2Request)
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
