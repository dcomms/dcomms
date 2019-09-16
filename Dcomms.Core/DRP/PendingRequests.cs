using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    ///// <summary>
    ///// instance exists when SynAck response is sent and 
    ///// </summary>
    //class PendingAcceptedRegisterRequest : IDisposable
    //{
    //    enum State
    //    {
    //        sentSynAck,
    //        receivedNextHopResponseToSynAck, 
    //        receivedAck,
    //        disposed
    //    }
    //    State _state;
      
    //    DateTime _createdAtUtc; // is taken from local clock
    //    PendingLowLevelUdpRequest _pendingSynAckRequest;

    //    TimeSpan CurrentRetransmissionTimeout;
    //    //  DateTime LatestTxUTC; // is taken from local clock
    //    //  int TxPacketsCount; // is incremented when UDP packet is retransmitted

    //    // ConnectedDrpPeer ReceivedFrom;
    //    //  ConnectedDrpPeer ProxiedTo; // null if terminated by local peer


    //    readonly RegisterSynPacket _registerSynPacket;
    //    readonly RegisterSynAckPacket _registerSynAckPacket;
    //    readonly byte[] _localEcdhe25519PrivateKey;
    //    readonly byte[] _registerSynAckUdpPayload;
    //    readonly DrpPeerEngine _drpPeerEngine;

    //    PendingLowLevelUdpRequest RegisterSynAckLowLevelUdpRequest; //todo retransmit regSynAck  until NextHopAckPacket   (if sent from proxier) or until RegAck (if REQ was sent by A)

    //    internal ConnectedDrpPeer NewConnectionToRequester; // goes into "connected peers" only after registerACK // or gets disposed on timeout

    //    /// <summary>
    //    /// is executed when SYNACK is transmitted by responder
    //    /// </summary>
    //    public PendingAcceptedRegisterRequest(DrpPeerEngine drpPeerEngine, RegisterSynPacket registerSynPacket, RegisterSynAckPacket registerSynAckPacket, 
    //        byte[] registerSynAckUdpPayload, byte[] localEcdhe25519PrivateKey, DateTime registerSynTimeUtc)
    //    {
    //        _drpPeerEngine = drpPeerEngine;
    //        _localEcdhe25519PrivateKey = localEcdhe25519PrivateKey;
    //        _registerSynPacket = registerSynPacket;
    //        _registerSynAckPacket = registerSynAckPacket;
    //        _registerSynAckUdpPayload = registerSynAckUdpPayload;
    //        _createdAtUtc = registerSynTimeUtc;
            
    //       // _lastTimeTransmittedSynAck = registerSynTimeUtc + drpPeerEngine.Configuration.;
    //    }
    //    public void Dispose()
    //    {
    //        _state = State.disposed;
    //        if (NewConnectionToRequester != null)
    //            NewConnectionToRequester.Dispose();
    //    }

    //    public void OnTimer_100ms(DateTime timeNowUTC, out bool needToRestartLoop)
    //    {
    //        needToRestartLoop = false;

    //        //switch (_state)
    //        //{
    //        //    case State.sentSynAck:
    //        //        if (timeNowUTC > _lastTimeTransmittedSynAck)
    //        //        //todo retransmit regSynAck  until NextHopAckPacket   (if sent from proxier)        or until RegAck (if REQ was sent by A)
    //        //        //             SendPacket(_registerSynAckUdpPayload, remoteEndpoint);
    //        //        break;
    //        //}

                       
            
            
    //        // dispose the instance in timer expiry
    //        //if (timeNowUTC > _createdAtUtc + _drpPeerEngine.Configuration.PendingRegisterRequestsTimeout)
    //        //{
    //        //    _drpPeerEngine.PendingAcceptedRegisterRequests.Remove(this._registerSynPacket.RequesterPublicKey_RequestID);
    //        //    this.Dispose();
    //        //}
    //    }

    //    public void OnReceivedAck()
    //    {
    //        //todo call this
    //        // pass to "connected peers"   // set NewConnectionToRequester=null (will not be disposed by this instance)

    //        _state = State.receivedAck;
    //    }


    //    public void OnNextHopResponse()
    //    {
    //        //todo call this
    //        _state = State.receivedNextHopResponseToSynAck;

    //    }
    //}

    //class PendingInviteRequestState : PendingRequest
    //{
    //    // requestID={RequesterPublicKey|DestinationResponderPublicKey}
    //    RegistrationPublicKey RequesterPublicKey; // A public key 
    //    RegistrationPublicKey DestinationResponderPublicKey; // B public key
    //}
}
