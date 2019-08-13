using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// A=requester
    /// B=responder
    /// A->N->X->B1
    /// 
    /// requestID={RequesterPublicKey|DestinationResponderPublicKey}
    /// </summary>
    class InviteRequestPacket
    {
        P2pConnectionToken32 SenderToken32;
        byte ReservedFlagsMustBeZero;


        uint Timestamp32S;
        byte[] DirectChannelEndointA_encryptedByResponderPublicKey; // with salt // can be decrypted only by B
        /// <summary>
        /// todo: look at noise protocol
        /// todo: look at ECIES
        /// </summary>
        byte[] DirectChannelSecretBA_encryptedByResponderPublicKey;

        RegistrationPublicKey RequesterPublicKey; // A public key 
        RegistrationPublicKey DestinationResponderPublicKey; // B public key
        byte[] RequesterSignature;

        byte NumberOfHopsRemaining; // max 10 // is decremented by peers

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        HMAC SenderHMAC;
        NextHopAckSequenceNumber16 NhaSeq16;
    }
    /// <summary>
    /// B1->X->N->A (rejected/confirmed)
    /// requestID={RequesterPublicKey|DestinationResponderPublicKey}
    /// </summary>
    class InviteResponsePacket
    {
        P2pConnectionToken32 SenderToken32;
        byte ReservedFlagsMustBeZero;
        
        uint InviteRequestTimestamp32S;
        RegistrationPublicKey RequesterPublicKey; // A public key 
        RegistrationPublicKey DestinationResponderPublicKey; // B public key

        DrpResponderStatusCode StatusCode;
        byte[] DirectChannelEndointB_encryptedByRequesterPublicKey;
        byte[] DirectChannelSecretAB_encryptedByRequesterPublicKey;
        byte[] ResponderMessage_encryptedByRequesterPublicKey; // messenger top-level protocol
        byte[] ResponderSignature;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        HMAC SenderHMAC;
        NextHopAckSequenceNumber16 NhaSeq16;
    }
}
