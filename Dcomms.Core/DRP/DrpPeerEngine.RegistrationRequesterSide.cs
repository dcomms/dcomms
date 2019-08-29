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
                       
            _engineThreadQueue.Enqueue(async () =>
            {
                var r = await BeginRegister2(registrationConfiguration, user);
                if (cb != null) cb(r);
            });
            
        }
        public LocalDrpPeer CreateLocalPeer(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            var localDrpPeer = new LocalDrpPeer(this, registrationConfiguration, user);
            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationPublicKey, localDrpPeer);
            return localDrpPeer;
        }
        async Task<LocalDrpPeer> BeginRegister2(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            WriteToLog_reg_requesterSide_detail($"@BeginRegister2() engine thread");

            var localDrpPeer = CreateLocalPeer(registrationConfiguration, user);
            if (registrationConfiguration.EntryPeerEndpoints.Length != 0)
            {
                if (Configuration.LocalForcedPublicIpForRegistration == null)
                {
                    WriteToLog_reg_requesterSide_detail($"resolving local public IP...");
                    var localPublicIp = await SendPublicIpAddressApiRequestAsync("http://api.ipify.org/");
                    if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://ip.seeip.org/");
                    if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://bot.whatismyipaddress.com");
                    if (localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");

                    localDrpPeer.LocalPublicIpAddressForRegistration = new IPAddress(localPublicIp);
                    WriteToLog_reg_requesterSide_detail($"resolved local public IP = {localDrpPeer.LocalPublicIpAddressForRegistration}");
                    WriteToLog_reg_requesterSide_detail($"@engine thread");
                    await _engineThreadQueue.EnqueueAsync();
                }
                else
                    localDrpPeer.LocalPublicIpAddressForRegistration = Configuration.LocalForcedPublicIpForRegistration;


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
            var registerPow1RequestPacket = GenerateRegisterPow1RequestPacket(localDrpPeer.LocalPublicIpAddressForRegistration.GetAddressBytes(), Timestamp32S);

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
            RegisterSynPacket syn;
            PongPacket pong;
            PendingLowLevelUdpRequest pendingPingRequest;
            try
            {
                #region register SYN
                // calculate PoW2
                syn = new RegisterSynPacket
                {
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    Timestamp32S = Timestamp32S,
                    MinimalDistanceToNeighbor = 0,
                    NumberOfHopsRemaining = 10,
                    RequesterEcdhePublicKey = new EcdhPublicKey { ecdh25519PublicKey = connectionToNeighbor.LocalEcdhe25519PublicKey },
                    NhaSeq16 = GetNewNhaSeq16(),
                    EpEndpoint = epEndpoint
                };
                WriteToLog_reg_requesterSide_detail($"calculating PoW2");
                GenerateRegisterSynPow2(syn, pow1ResponsePacket.ProofOfWork2Request);
                syn.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => syn.GetCommonRequesterProxierResponderFields(w, false),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                    );
                var synToSynAckStopwatch = Stopwatch.StartNew();

                WriteToLog_reg_requesterSide_detail($"sending syn, waiting for NextHopAck. NhaSeq16={syn.NhaSeq16}");
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNextHopAck(syn.Encode(null), epEndpoint, syn.NhaSeq16);

                #endregion

                #region wait for RegisterSynAckPacket
                WriteToLog_reg_requesterSide_detail($"waiting for synAck");
                var registerSynAckPacketData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(epEndpoint,
                                RegisterSynAckPacket.GetScanner(syn.RequesterPublicKey_RequestID, syn.Timestamp32S),
                                DateTimeNowUtc, Configuration.RegSynAckRequesterSideTimoutS                               
                            ));
                if (registerSynAckPacketData == null) throw new DrpTimeoutException();
                var synAck = RegisterSynAckPacket.DecodeAndVerifyAtRequester(registerSynAckPacketData, syn, connectionToNeighbor);
                WriteToLog_reg_requesterSide_detail($"verified synAck");
                #endregion

                connectionToNeighbor.LocalEndpoint = synAck.RequesterEndpoint;
                connectionToNeighbor.RemotePeerPublicKey = synAck.ResponderPublicKey;
                synToSynAckStopwatch.Stop();
                var synToSynAckTimeMs = synToSynAckStopwatch.Elapsed.TotalMilliseconds;

                #region send ACK, encode local IP
                var ack = new RegisterAckPacket
                {
                    RegisterSynTimestamp32S = syn.Timestamp32S,
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    NhaSeq16 = GetNewNhaSeq16()
                };            
                ack.ToRequesterTxParametersEncrypted = connectionToNeighbor.EncryptAtRegisterRequester(syn, synAck, ack);
                connectionToNeighbor.InitializeNeighborTxRxStreams(syn, synAck, ack);
                ack.RequesterHMAC = connectionToNeighbor.GetSharedHmac(w => ack.GetCommonRequesterProxierResponderFields(w, false, true));

                WriteToLog_reg_requesterSide_detail($"sending ACK, waiting for NextHopAck");
                RespondToRequestAndRetransmissions(registerSynAckPacketData, ack.Encode(null), epEndpoint);
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNextHopAck(null, epEndpoint, ack.NhaSeq16);
                #endregion

                var neighborWaitTimeMs = synToSynAckTimeMs * 0.5 - 100; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                if (neighborWaitTimeMs > 20)
                {
                    await _engineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the registerACK reaches neighbor N via peers
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
                    NhaSeq16 = GetNewNhaSeq16()
                };
                cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => connectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                WriteToLog_reg_requesterSide_detail($"sending CFM, waiting for NHACK");
                await OptionallySendUdpRequestAsync_Retransmit_WaitForNextHopAck(cfm.Encode(null), epEndpoint, cfm.NhaSeq16);
                WriteToLog_reg_requesterSide_detail($"received NHACK to CFM");
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

            var rnd = new Random();
            for (; ; )
            {
                rnd.NextBytes(packet.ProofOfWork1);
                if (Pow1IsOK(packet, clientPublicIp)) break;
            }

            packet.Pow1RequestId = (uint)rnd.Next();
            return packet;
        }
        void GenerateRegisterSynPow2(RegisterSynPacket packet, byte[] proofOfWork2Request)
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
