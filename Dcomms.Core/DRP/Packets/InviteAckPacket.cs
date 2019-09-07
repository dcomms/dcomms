using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// B1->X->N->A (rejected/confirmed)
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
