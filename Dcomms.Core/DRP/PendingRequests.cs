using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// instance exists when SynAck response is sent and 
    /// </summary>
    class PendingAcceptedRegisterRequest : IDisposable
    {
        enum State
        {
            sentSynAck,
            receivedAck,
        }
        State _state;
      
        DateTime _createdAtUtc; // is taken from local clock
      //  DateTime LatestTxUTC; // is taken from local clock
      //  int TxPacketsCount; // is incremented when UDP packet is retransmitted

       // ConnectedDrpPeer ReceivedFrom;
        //  ConnectedDrpPeer ProxiedTo; // null if terminated by local peer


        readonly RegisterSynPacket _registerSynPacket;
        readonly RegisterSynAckPacket _registerSynAckPacket;
        readonly byte[] _localEcdhe25519PrivateKey;
        readonly byte[] _registerSynAckUdpPayload;

        internal ConnectedDrpPeer NewConnectionToRequester; // goes into "connected peers" only after registerACK // or gets disposed on timeout

        public PendingAcceptedRegisterRequest(RegisterSynPacket registerSynPacket, RegisterSynAckPacket registerSynAckPacket, 
            byte[] registerSynAckUdpPayload, byte[] localEcdhe25519PrivateKey, DateTime registerSynTimeUtc)
        {
            _localEcdhe25519PrivateKey = localEcdhe25519PrivateKey;
            _registerSynPacket = registerSynPacket;
            _registerSynAckPacket = registerSynAckPacket;
            _registerSynAckUdpPayload = registerSynAckUdpPayload;
            _createdAtUtc = registerSynTimeUtc;

            NewConnectionToRequester = new ConnectedDrpPeer();
        }
        public void Dispose()
        {
            if (NewConnectionToRequester != null)
                NewConnectionToRequester.Dispose();
        }

        public void OnTimer_100ms(DateTime timeNowUTC, out bool needToRestartLoop)
        {
            //todo retransmit regSynAck  until NextHopAckPacket   (if sent from proxier)        or until RegAck (if SYN was sent by A)
            //             SendPacket(_registerSynAckUdpPayload, remoteEndpoint);

            // todo expire timer - delete the instance
            //    dispose();
        }

        public void OnReceivedAck()
        {
            // pass to "conencted peers"   // set NewConnectionToRequester=null (will not be disposed by this instance)

        }
    }

    //class PendingInviteRequestState : PendingRequest
    //{
    //    // requestID={RequesterPublicKey|DestinationResponderPublicKey}
    //    RegistrationPublicKey RequesterPublicKey; // A public key 
    //    RegistrationPublicKey DestinationResponderPublicKey; // B public key
    //}
}
