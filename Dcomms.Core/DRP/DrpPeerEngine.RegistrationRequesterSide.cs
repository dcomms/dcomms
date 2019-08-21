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
    /// registration, for requester peer (A): connect to the p2P network via rendezvous peer (RP)
    /// </summary>
    partial class DrpPeerEngine
    {
        /// <summary>
        /// returns control only when LocalDrpPeer is registered and ready for operation ("local user logged in")
        /// </summary>
        public async Task<LocalDrpPeer> RegisterAsync(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            await _engineThreadQueue.EnqueueAsync();

            var localDrpPeer = new LocalDrpPeer(this, registrationConfiguration, user);
            LocalPeers.Add(registrationConfiguration.LocalPeerRegistrationPublicKey, localDrpPeer);

            if (registrationConfiguration.RendezvousPeerEndpoints.Length != 0)
            {
                WriteToLog_reg_requesterSide_debug($"resolving local public IP...");
                var localPublicIp = await SendPublicIpAddressApiRequestAsync("http://api.ipify.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://ip.seeip.org/");
                if (localPublicIp == null) localPublicIp = await SendPublicIpAddressApiRequestAsync("http://bot.whatismyipaddress.com");
                if (localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");
                localDrpPeer.LocalPublicIpAddressForRegistration = new IPAddress(localPublicIp);
                WriteToLog_reg_requesterSide_debug($"resolved local public IP = {localDrpPeer.LocalPublicIpAddressForRegistration}");

                await _engineThreadQueue.EnqueueAsync();
                foreach (var rpEndpoint in registrationConfiguration.RendezvousPeerEndpoints) // try to connect to rendezvous peers, one by one
                {
                    if (MiscProcedures.EqualByteArrays(rpEndpoint.Address.GetAddressBytes(), localPublicIp) == true)
                    {
                        WriteToLog_reg_requesterSide_debug($"not connecting to RP {rpEndpoint}: same IP address as local public IP");
                    }
                    else
                    {
                        try
                        {
                            if (await RegisterAsync(localDrpPeer, rpEndpoint) == null)
                                continue;

                            //  on error or timeout try next rendezvous server
                        }
                        catch (Exception exc)
                        {
                            HandleExceptionWhileConnectingToRP(rpEndpoint, exc);
                        }
                    }
                }
            }
            else
            {
                WriteToLog_reg_requesterSide_debug($"resolving local public IP...");
            }

            return localDrpPeer;
        }

        /// <returns>null if registration failed with timeout or some error code</returns>
        public async Task<ConnectedDrpPeer> RegisterAsync(LocalDrpPeer localDrpPeer, IPEndPoint rpEndpoint) // engine thread
        {
            WriteToLog_reg_requesterSide_debug($"connecting to RP {rpEndpoint}...");

            #region PoW1
            var registerPow1RequestPacket = GenerateRegisterPow1RequestPacket(localDrpPeer.LocalPublicIpAddressForRegistration.GetAddressBytes(), Timestamp32S);

            // send register pow1 request
            var rpPow1ResponsePacketData = await SendUdpRequestAsync_Retransmit(
                        new PendingLowLevelUdpRequest(rpEndpoint,
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
            if (pow1ResponsePacket.StatusCode != RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
                throw new Pow1RejectedException(pow1ResponsePacket.StatusCode);
            #endregion

            _cryptoLibrary.GenerateEcdh25519Keypair(out var localEcdhe25519PrivateKey, out var localEcdhe25519PublicKey);
            var neighborConnection = new ConnectedDrpPeer(this, localDrpPeer, ConnectedDrpPeerInitiatedBy.localPeer);
            PingResponsePacket pingResponsePacket;
            PendingLowLevelUdpRequest pendingPingRequest;
            try
            {
                #region register SYN
                // calculate PoW2
                var registerSynPacket = new RegisterSynPacket
                {
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    Timestamp32S = Timestamp32S,
                    MinimalDistanceToNeighbor = 0,
                    NumberOfHopsRemaining = 10,
                    RequesterEcdhePublicKey = new EcdhPublicKey { ecdh25519PublicKey = localEcdhe25519PublicKey },
                    NhaSeq16 = GetNewNhaSeq16(),
                    RpEndpoint = rpEndpoint
                };
                GenerateRegisterSynPow2(registerSynPacket, pow1ResponsePacket.ProofOfWork2Request);
                registerSynPacket.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => registerSynPacket.GetCommonRequesterAndResponderFields(w, false),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey
                    );
                var synToSynAckStopwatch = Stopwatch.StartNew();
                await SendUdpRequestAsync_Retransmit_WaitForNextHopAck(registerSynPacket.Encode(null), rpEndpoint, registerSynPacket.NhaSeq16);

                #endregion

                #region wait for RegisterSynAckPacket
                var registerSynAckPacketData = await WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(rpEndpoint,
                                RegisterSynAckPacket.GetScanner(registerSynPacket.RequesterPublicKey_RequestID, registerSynPacket.Timestamp32S),
                                DateTimeNowUtc, Configuration.RegSynAckRequesterSideTimoutS                               
                            ));
                if (registerSynAckPacketData == null) throw new DrpTimeoutException();
                var registerSynAckPacket = RegisterSynAckPacket.DecodeAndVerifyAtRequester(registerSynAckPacketData,
                    registerSynPacket, localEcdhe25519PrivateKey, _cryptoLibrary, out var txParameters);
                #endregion

                neighborConnection.LocalEndpoint = registerSynAckPacket.RequesterEndpoint;
                neighborConnection.TxParameters = txParameters;
                neighborConnection.RemotePeerPublicKey = registerSynAckPacket.NeighborPublicKey;
                synToSynAckStopwatch.Stop();
                var synToSynAckTimeMs = synToSynAckStopwatch.Elapsed.TotalMilliseconds;

                #region send ACK, encode local IP
                var registerAckPacket = new RegisterAckPacket
                {
                    RegisterSynTimestamp32S = registerSynPacket.Timestamp32S,
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    NhaSeq16 = GetNewNhaSeq16()
                };
               // var localRxParamtersToEncrypt = new P2pStreamParameters
              //  {
               //     RemoteEndpoint = registerSynAckPacket.RequesterEndpoint, // comes from RP, and it is a subject of attack by RP or MITM on the way to RP
              //      RemotePeerToken32 = neighborConnection.LocalRxToken32
             //   };
                registerAckPacket.ToRequesterTxParametersEncrypted =
                    P2pStreamParameters.EncryptAtRegisterRequester(
                        txParameters.SharedDhSecret,
                        registerSynPacket, registerSynAckPacket, registerAckPacket,
                        neighborConnection,
                        _cryptoLibrary
                        );
                registerAckPacket.RequesterHMAC = txParameters.GetSharedHmac(_cryptoLibrary, w => registerAckPacket.GetCommonRequesterAndResponderFields(w, false, true));
                var registerAckPacketData = registerAckPacket.Encode(null);
                await SendUdpRequestAsync_Retransmit_WaitForNextHopAck(registerAckPacket.Encode(null), rpEndpoint, registerAckPacket.NhaSeq16);
                #endregion

                var neighborWaitTimeMs = synToSynAckTimeMs * 0.5 - 100; if (neighborWaitTimeMs < 0) neighborWaitTimeMs = 0;
                if (neighborWaitTimeMs > 20)
                {
                    await _engineThreadQueue.WaitAsync(TimeSpan.FromMilliseconds(neighborWaitTimeMs)); // wait until the registerACK reaches neighbor N via peers
                }

                // get shared IV from hashes of syn,synack,ack packets (common fields)
                neighborConnection.TxParameters.InitializeNeighborTxRxStreams(registerSynPacket, registerSynAckPacket, registerAckPacket, _cryptoLibrary);
                
                localDrpPeer.ConnectedPeers.Add(neighborConnection);

                #region send ping request directly to neighbor N, retransmit               
                var pingRequestPacket = neighborConnection.CreatePingRequestPacket(true);
                pendingPingRequest = new PendingLowLevelUdpRequest(txParameters.RemoteEndpoint,
                                PingResponsePacket.GetScanner(neighborConnection.LocalRxToken32, pingRequestPacket.PingRequestId32),
                                DateTimeNowUtc,
                                Configuration.InitialPingRequests_ExpirationTimeoutS,
                                pingRequestPacket.Encode(),
                                Configuration.InitialPingRequests_InitialRetransmissionTimeoutS,
                                Configuration.InitialPingRequests_RetransmissionTimeoutIncrement
                            );

                var pingResponsePacketData = await SendUdpRequestAsync_Retransmit(pendingPingRequest); // wait for pingResponse from N
                if (pingResponsePacketData == null) throw new DrpTimeoutException();
                pingResponsePacket = PingResponsePacket.DecodeAndVerify(_cryptoLibrary,
                    pingResponsePacketData, pingRequestPacket, neighborConnection,
                    true, registerSynPacket, registerSynAckPacket);
                #endregion
            }
            catch
            {
                neighborConnection.Dispose(); // remove from token16 table
                throw;
            }


            neighborConnection.OnReceivedVerifiedPingResponse(pingResponsePacket, pendingPingRequest.ResponseReceivedAtUtc.Value,
                pendingPingRequest.ResponseReceivedAtUtc.Value - pendingPingRequest.InitialTxTimeUTC.Value);

            #region send registration confirmation packet to RP->X->N
            try
            {
                var registerConfirmationPacket = new RegisterConfirmationPacket
                {
                    RequesterPublicKey_RequestID = localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                    NeighborP2pConnectionSetupSignature = pingResponsePacket.P2pConnectionSetupSignature,
                    NhaSeq16 = GetNewNhaSeq16()
                };
                registerConfirmationPacket.RequesterSignature = RegistrationSignature.Sign(_cryptoLibrary,
                    w => registerConfirmationPacket.GetCommonFields(w, true),
                    localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPrivateKey);
                await SendUdpRequestAsync_Retransmit_WaitForNextHopAck(registerConfirmationPacket.Encode(null), rpEndpoint, registerConfirmationPacket.NhaSeq16);
            }
            catch (Exception exc)
            {  // we ingnore exceptions here, just wite warning to log.  the connection is alive already, as direct ping channel to neighbor is set up 
                WriteToLog_reg_requesterSide_warning($"... registration confirmation request failed: {exc}");
            }
            #endregion

            return neighborConnection;
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
                HandleException(exc, $"public IP address API request to {url} failed");
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
