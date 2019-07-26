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
            // send register pow request
            // wait for response
            // on error or timeout try next rendezvous server

        }

        void ProcessUdpPacket(IPEndPoint remoteEndpoint, byte[] data) // receiver thread
        {
            // parse packet
            // if register  syn:
        }
        void OnTimer_1s() // manager thread
        {

        }

        public void BeginSendInvite(RegistrationPublicKey localPeerRegistrationPublicKey, RegistrationPublicKey remotePeerRegistrationPublicKey, byte[] message, Action<DrpResponderStatusCode> callback)
        {
            // find RegisteredLocalDrpPeer

            // find closest neighbor to destination

            // send invite

            // add txrequest state 
        }
    }
    class RegisteredLocalDrpPeer
    {
        List<ConnectedDrpPeer> ConnectedPeers; // neighbors
        ConnectedDrpPeer GetClosestConnectedPeer(RegistrationPublicKey targetRegistrationPublicKey)
        {
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


    class ConnectedDrpPeer
    {
        RegistrationPublicKey RemotePeerPublicKey;
        SecretHeyForHmac SecretKeyForHmac;

        ConnectedDrpPeerRating Rating;

        IirFilterCounter RxRateRps;
        
        IirFilterCounter TxRateRps;
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
