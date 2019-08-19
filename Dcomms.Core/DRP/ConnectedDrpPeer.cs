﻿using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    public enum ConnectedDrpPeerInitiatedBy
    {
        localPeer, // local peer connected to remote peer via REGISTER procedure
        remotePeer // remote peer connected to local peer via REGISTER procedure
    }
    /// <summary>
    /// connected or connecting peer
    /// </summary>
    public class ConnectedDrpPeer: IDisposable
    {
        public ConnectedDrpPeerInitiatedBy InitiatedBy;
        public P2pStreamParameters TxParameters;
        public RegistrationPublicKey RemotePeerPublicKey;
        public readonly P2pConnectionToken32 LocalRxToken32; // is generated by local peer
        public IPEndPoint LocalEndpoint;
        
        ConnectedDrpPeerRating Rating;
        readonly Random _rngForPingRequestId32 = new Random();

        IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        PingRequestPacket _latestPingRequestPacketSent;
        PingRequestPacket _latestPingRequestPacketReceived;// float MaxTxInviteRateRps, MaxTxRegiserRateRps; // sent by remote peer via ping
        DateTime? _latestPingRequestPacketSentTime;
        
        PingResponsePacket _latestReceivedPingResponsePacket;
        TimeSpan? _latestRequestResponseDelay;

        IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
       // List<TxRegisterRequestState> PendingTxRegisterRequests;
      //  List<TxInviteRequestState> PendingTxInviteRequests;

        DateTime? _lastTimeSentPingRequest;
        DateTime _lastTimeCreatedOrReceivedVerifiedResponsePacket; // is updated when received "alive" signal from remote peer: ping response or ...
        readonly DrpPeerEngine _engine;
        readonly LocalDrpPeer _localDrpPeer;
        public ConnectedDrpPeer(DrpPeerEngine engine, LocalDrpPeer localDrpPeer, ConnectedDrpPeerInitiatedBy initiatedBy)
        {
            _localDrpPeer = localDrpPeer;
            _engine = engine;
            _lastTimeCreatedOrReceivedVerifiedResponsePacket = _engine.DateTimeNowUtc;
            InitiatedBy = initiatedBy;
            P2pConnectionToken32 localRxToken32 = null;
            for (int i = 0; i < 100; i++)
            {
                localRxToken32 = new P2pConnectionToken32 { Token32 = (uint)_engine.InsecureRandom.Next() };
                var rToken16 = localRxToken32.Token16;
                if (_engine.ConnectedPeersByToken16[rToken16] == null)
                {
                    _engine.ConnectedPeersByToken16[rToken16] = this;               
                }
            }
            if (localRxToken32 == null) throw new InsufficientResourcesException();

            LocalRxToken32 = localRxToken32;
        }
        public void Dispose()
        {
            _localDrpPeer.ConnectedPeers.Remove(this);
            _engine.ConnectedPeersByToken16[LocalRxToken32.Token16] = null;
        }

        public PingRequestPacket CreatePingRequestPacket(bool requestP2pConnectionSetupSignature)
        {
            var r = new PingRequestPacket
            {
                SenderToken32 = TxParameters.RemotePeerToken32,
                MaxRxInviteRateRps = 10, //todo get from some local capabilities   like number of neighbors
                MaxRxRegisterRateRps = 10, //todo get from some local capabilities   like number of neighbors
                PingRequestId32 = (uint)_rngForPingRequestId32.Next(),
            };
            if (requestP2pConnectionSetupSignature)
                r.Flags |= PingRequestPacket.Flags_P2pConnectionSetupSignatureRequested;
            r.SenderHMAC = TxParameters.GetSharedHmac(_engine.CryptoLibrary, r.GetSignedFieldsForSenderHMAC);
            return r;
        }
        public void OnTimer100ms(DateTime timeNowUTC, out bool needToRestartLoop) // engine thread
        {
            needToRestartLoop = false;
            try
            {
                // remove timed out connected peers (neighbors)
                if (timeNowUTC > _lastTimeCreatedOrReceivedVerifiedResponsePacket + _engine.Configuration.ConnectedPeersRemovalTimeout)
                {
                    this.Dispose(); // remove dead connected peers (no reply to ping)
                    needToRestartLoop = true;
                    return;
                }

                // send ping requests  when idle (no other "alive" signals from remote peer)
                if (timeNowUTC > _lastTimeCreatedOrReceivedVerifiedResponsePacket + _engine.Configuration.PingRequestsInterval)
                {
                    if (_lastTimeSentPingRequest == null ||
                        timeNowUTC > _lastTimeSentPingRequest.Value.AddSeconds(_latestRequestResponseDelay.Value.TotalSeconds * _engine.Configuration.PingRetransmissionInterval_RttRatio))
                    {
                       _lastTimeSentPingRequest = timeNowUTC;
                       SendPingRequestOnTimer();
                    }
                }
            }
            catch (Exception exc)
            {
                _engine.HandleGeneralException($"error in ConnectedDrpPeer timer procedure: {exc}");
            }
        }
        void SendPingRequestOnTimer()
        {
            var pingRequestPacket = CreatePingRequestPacket(false);
            _engine.SendPacket(pingRequestPacket.Encode(), TxParameters.RemoteEndpoint);
            _latestPingRequestPacketSent = pingRequestPacket;
            _latestPingRequestPacketSentTime = _engine.DateTimeNowUtc;
        }
        void SendPacket(byte[] udpPayload)
        {
            _engine.SendPacket(udpPayload, TxParameters.RemoteEndpoint);
        }
        public void OnReceivedVerifiedPingResponse(PingResponsePacket pingResponsePacket, DateTime responseReceivedAtUTC, TimeSpan? requestResponseDelay)
        {
            _lastTimeCreatedOrReceivedVerifiedResponsePacket = responseReceivedAtUTC;
            _latestReceivedPingResponsePacket = pingResponsePacket;
            // todo process requestResponseDelay to measure RTT and use it for rating
            if (requestResponseDelay.HasValue)
                OnMeasuredRequestResponseDelay(requestResponseDelay.Value);
        }

        void OnMeasuredRequestResponseDelay(TimeSpan requestResponseDelay)
        {
            _latestRequestResponseDelay = requestResponseDelay;
        }

        internal void OnReceivedPingResponsePacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData, DateTime receivedAtUtc) // engine thread
        {
            if (remoteEndpoint.Equals(this.TxParameters.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pingResponsePacket = PingResponsePacket.DecodeAndVerify(_engine.CryptoLibrary,
                    udpPayloadData,
                    null, this,
                    false, null, null);
                OnReceivedVerifiedPingResponse(pingResponsePacket, receivedAtUtc, null);

                if (_latestPingRequestPacketSent?.PingRequestId32 == pingResponsePacket.PingRequestId32)
                {
                    //  we have multiple previously sent ping requests; and 1 most recent one
                    //  if pingResponse.PingRequestId32   matches to most recently sent ping request ->   update RTT stats
                    OnMeasuredRequestResponseDelay(receivedAtUtc - _latestPingRequestPacketSentTime.Value);
                }
                // else it is out-of-sequence ping response
            }
            catch (PossibleMitmException exc)
            {
                _engine.HandleGeneralException($"breaking p2p connection on MITM exception: {exc}");
                this.Dispose();
            }
        }

        internal void OnReceivedPingRequestPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData) // engine thread
        {
            if (remoteEndpoint.Equals(this.TxParameters.RemoteEndpoint) == false)
            {
                _engine.OnReceivedUnauthorizedSourceIpPacket(remoteEndpoint);
                return;
            }
            try
            {
                var pingRequestPacket = PingRequestPacket.DecodeAndVerify(udpPayloadData, this, _engine.CryptoLibrary);
                _latestPingRequestPacketReceived = pingRequestPacket;
                var pingResponsePacket = new PingResponsePacket
                {
                    PingRequestId32 = pingRequestPacket.PingRequestId32,
                    SenderToken32 = TxParameters.RemotePeerToken32,
                };
                if ((pingRequestPacket.Flags & PingRequestPacket.Flags_P2pConnectionSetupSignatureRequested) != 0)
                {
                    //   pingResponsePacket.P2pConnectionSetupSignature = xxx;
                    // todo implement it for N: pass reg.syn packets into here
                    throw new NotImplementedException();
                }
                pingResponsePacket.SenderHMAC = TxParameters.GetSharedHmac(_engine.CryptoLibrary, pingResponsePacket.GetSignedFieldsForSenderHMAC);            
                _engine.SendPacket(pingResponsePacket.Encode(), TxParameters.RemoteEndpoint);
            }
            catch (PossibleMitmException exc)
            {
                _engine.HandleGeneralException($"breaking P2P connection on MITM exception: {exc}");
                this.Dispose();
            }
        }
       
    }
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
}
