using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dcomms.DRP
{
    class DrpPeer: IDisposable
    {
        bool _disposing;
        Thread _managerThread;
        Thread _receiverThread;
        UdpClient _socket;
        
        public DrpPeer(DrpPeerConfiguration configuration, IDrpPeerUser user)
        {

        }
        public void Dispose()
        {
        }

        Dictionary<DrpPeerRegistrationConfiguration, RegisteredLocalDrpPeer> RegisteredLocalPeers;
        public void BeginRegister(DrpPeerRegistrationConfiguration registrationConfiguration)
        {
            // todo
            // send register pow request
            // wait for response
            // on error or timeout try next rendezvous server

        }

        void ProcessUdpPacket(IPEndPoint remoteEndpoint, byte[] data) // receiver thread
        {
            // parse packet
            // process register pow  like in ccp
            // see which peer sends this packet, authentcate by HMAC
            // update and limit rx packet rate - blacklist regID
            // process register syn:
            //   see if local peer is good neighbor, reply
            //   decrement nhops, check if it is not 0, proxy to some neighbor:
            //       subroutine create requestViaConnectedPeer

            // process reponses: pass to original requester, verify responder signature and update rating
            //   when request is complete, clean state

        }

        void OnTimer_1s() // manager thread
        {
            // for every connected peer
            //   update IIR counters for rates
            //   send ping in case of inactivity
            //   remove dead connected peers (no reply to ping)
            //   retransmit packets
            //   clean timed out requests, raise timeout events

            // expand neighborhood

            // send test packets between local registered peers
        }

        public void BeginSendInvite(RegistrationPublicKey localPeerRegistrationPublicKey, RegistrationPublicKey remotePeerRegistrationPublicKey, byte[] message, Action<DrpResponderStatusCode> callback)
        {
            // find RegisteredLocalDrpPeer

            // find closest neighbor to destination

            // send invite

            // subroutine create requestViaConnectedPeer
        }

        /// <summary>
        /// creates an instance of TxRequestState, starts retransmission timer
        /// </summary>
        bool TrySendRequestViaConnectedPeer(ConnectedDrpPeer connectedPeer, RegistrationPublicKey remotePeerRegistrationPublicKey)
        {
            // assert tx rate is not exceeded  -- return false

            // create an instance of TxRequestState, add it to list

            // send packet to peer


            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// "contact point" of local user in the regID space
    /// </summary>
    class RegisteredLocalDrpPeer
    {
        List<ConnectedDrpPeer> ConnectedPeers; // neighbors
        ConnectedDrpPeer GetClosestNonFloodedConnectedPeer(RegistrationPublicKey targetRegistrationPublicKey)
        {
            // enumerate conn. peers
            //   skip flooded tx connections (where tx rate is exceeded)
            //   get distance = xor
            //   
            throw new NotImplementedException();
        }
    }


    class DrpPeerConfiguration
    {
        ushort? LocalPort;
        IPEndPoint[] RendezvousPeers;
    }
    class DrpPeerRegistrationConfiguration
    {
        RegistrationPublicKey LocalPeerRegistrationPublicKey;
        RegistrationPrivateKey LocalPeerRegistrationPrivateKey;
        int NumberOfNeighborsToKeep;
    }

    interface IDrpPeerUser
    {
        void OnReceivedMessage(byte[] message);
    }

    enum DrpPeerConnectionInitiatedBy
    {
        localPeer, // local peer connected to remote peer via REGISTER procedure
        remotePeer // re mote peer connected to local peer via REGISTER procedure
    }
    class ConnectedDrpPeer
    {
        DrpPeerConnectionInitiatedBy InitiatedBy;
        RegistrationPublicKey RemotePeerPublicKey;
        SecretHeyForHmac SecretKeyForHmac;

        ConnectedDrpPeerRating Rating;

        IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        float MaxTxInviteRateRps, MaxTxRegiserRaateRps; // sent by remote peer via ping
        IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
        List<TxRegisterRequestState> PendingTxRegisterRequests;
        List<TxInviteRequestState> PendingTxInviteRequests;
    }
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        IirFilterCounter RegisterRequestsSent;
        IirFilterCounter RegisterRequestsSuccessfullyCompleted; // target of sybil-looped-traffic attack

        IirFilterCounter InviteRequestsSent;
        IirFilterCounter InviteRequestsSuccessfullyCompleted; // target of sybil-looped-traffic attack
    }

    class TxRequestState
    {
        DateTime WhenCreatedUTC; // is taken from local clock
        DateTime LatestTxUTC; // is taken from local clock
        int TxPacketsCount; // is incremented when UDP packet is retransmitted

        ConnectedDrpPeer ReceivedFrom;
        ConnectedDrpPeer ProxiedTo;
    }

    class TxRegisterRequestState: TxRequestState
    {
        RegistrationPublicKey RequesterPublicKey_RequestID;
    }

    class TxInviteRequestState: TxRequestState
    {
        // requestID={RequesterPublicKey|DestinationResponderPublicKey}
        RegistrationPublicKey RequesterPublicKey; // A public key 
        RegistrationPublicKey DestinationResponderPublicKey; // B public key
    }
}
