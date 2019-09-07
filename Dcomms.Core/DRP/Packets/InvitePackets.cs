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
    class InviteSynPacket
    {
        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public P2pConnectionToken32 SenderToken32;
        byte Flags;

        public uint Timestamp32S;
        public RegistrationPublicKey RequesterPublicKey; // A public key 
        public RegistrationPublicKey ResponderPublicKey; // B public key
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new DirectChannel connection

        public RegistrationSignature RequesterSignature;

        public byte NumberOfHopsRemaining; // max 10 // is decremented by peers

        public NextHopAckSequenceNumber16 NhaSeq16;
        
        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC SenderHMAC;
    }
    /// <summary>
    /// B1->X->N->A (rejected/confirmed)
    /// requestID={RequesterPublicKey|DestinationResponderPublicKey}
    /// </summary>
    class InviteAckPacket
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
