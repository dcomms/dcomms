using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// instance exists when SynAck response is sent and 
    /// </summary>
    class PendingAcceptedRegisterRequest 
    {
        RegistrationPublicKey RequesterPublicKey_RequestID;
        DateTime WhenCreatedUTC; // is taken from local clock
      //  DateTime LatestTxUTC; // is taken from local clock
      //  int TxPacketsCount; // is incremented when UDP packet is retransmitted

        ConnectedDrpPeer ReceivedFrom;
        //  ConnectedDrpPeer ProxiedTo; // null if terminated by local peer


        readonly RegisterSynPacket _registerSynPacket;
        public PendingAcceptedRegisterRequest(RegisterSynPacket registerSynPacket)
        {
            _registerSynPacket = registerSynPacket;
        }

        public void OnReceivedAck()
        {

        }
    }

    //class PendingInviteRequestState : PendingRequest
    //{
    //    // requestID={RequesterPublicKey|DestinationResponderPublicKey}
    //    RegistrationPublicKey RequesterPublicKey; // A public key 
    //    RegistrationPublicKey DestinationResponderPublicKey; // B public key
    //}
}
