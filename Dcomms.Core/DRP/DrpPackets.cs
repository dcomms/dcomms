using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// A = requester
    /// RP = rendezvous server, proxy peer
    /// is sent from A to RP when A connects to the P2P network
    /// protects system against IP spoofing
    /// </summary>
    class RegisterPowRequestPacket
    {
        byte ReservedFlagsMustBeZero;
        uint Timestamp;
        // comes from IP level  byte[] RequesterPublicIp; 

        byte[] ProofOfWork1;  
        // sha512(timestamp|requesterPublicIp|ProofOfWork1) has byte[6]=7 
        // includes powType 
        //todo: consider argon2-based PoW;  bcrypt, scrypt -based:  slow on GPUs
    }
    /// <summary>
    /// verifies UDP/IP path from RP to A, check UDP.sourceIP
    /// can be used for UDP reflection attacks
    /// </summary>
    class RegisterPowResponsePacket
    {
        byte ReservedFlagsMustBeZero;
        RegisterPowResponseStatusCode StatusCode;      
        byte[] ProofOrWork2Request;
    }
    enum RegisterPowResponseStatusCode
    {
        succeeded_Pow1Challenge,
                
        rejected, // is sent if peer in "developer" mode only
        rejected_badtimestamp, // is sent if peer in "developer" mode only (???) peer is responsible for his clock, using 3rd party time servers
        rejected_badPublicIp // is sent if peer in "developer" mode only
        // also: ignored
    }


    /// <summary>
    /// is sent from A to RP
    /// is sent from RP to M, from M to N
    /// is sent over established P2P UDP channels that are kept alive by pings.  
    /// sender proxy is authenticated by source IP:UDP port, and it can be spoofed.
    /// if spoofing is suspected, a rejected_badRequestId DrpNextHopResponsePacket is sent
    /// </summary>
    class RegisterSynPacket
    {
        byte ReservedFlagsMustBeZero;
 
        RegistrationPublicKey RequesterPublicKey_RequestID; // used to verify signature // used also as request ID
        /// <summary>
        /// against flood by this packet in future, without A (against replay attack)
        /// </summary>
        uint Timestamp;
        
        uint MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood
        byte[] RequesterSignature; // is verified by N, MAY be verified by proxies

        /// <summary>
        /// is transmitted only from A to RP
        /// sha512(RequesterPublicKey_RequestID|ProofOrWork2Request|ProofOfWork2) has byte[6]=7  
        /// includes powType 
        /// </summary>
        byte[] ProofOfWork2;
        /// <summary>
        /// RP knows size of network.  he knows distance RP-A.  he knows "average number of hops" for this distance
        /// RP limits this field by "average number of hops"
        /// is decremented by peers
        /// </summary>
        byte NumberOfHops;

        /// <summary>
        /// signature of latest proxy sender: RP,M,X
        /// is NULL for A->RP packet
        /// uses common secret of neighbors within p2p connection
        /// </summary>
        HMAC SenderHMAC;
    }
    /// <summary>
    /// is sent from next hop to previous hop, when the next hop receives some packet from neighbor
    /// </summary>
    class DrpNextHopResponsePacket
    {
        byte ReservedFlagsMustBeZero;
        RegistrationPublicKey RequesterPublicKey_RequestID;
        DrpNextHopResponseCode StatusCode;
        HMAC SenderHMAC;
    }
    enum DrpNextHopResponseCode
    {
        received, // is sent to previous hop immediately when packet is proxied, to avoid retransmissions      
        rejected_overloaded,
        rejected_rateExceeded, // anti-ddos
    }
    
    /// <summary>
    /// is sent from neighbor=responder=N to M, from M to RP, from RP to A
    /// ответ от N к A идет по тому же пути, узлы помнят обратный путь по RequestId
    /// </summary>
    class RegisterSynAckPacket
    {
        byte ReservedFlagsMustBeZero;
        DrpResponderStatusCode NeighborStatusCode;
        /// <summary>
        /// not null only for (status=connected) (N->X-M-RP-A)
        /// IP address of N with salt, encrypted for A
        /// </summary>
        EncryptedP2pStreamParameters NeighborEndpoint_EncryptedByRequesterPublicKey;
       

        RegistrationPublicKey RequesterPublicKey_RequestID;
        RegistrationPublicKey NeighborPublicKey; // pub key of RP, M, N
        byte[] NeighborSignature; // signature of entire packet

        HMAC SenderHMAC; // is NULL for RP->A

        IPEndPoint EndpointA; // is sent only from RP to A to provide public IP:port

    }

    /// <summary>
    /// remote peer -> local peer via REGISTER channel
    /// parameters to connect to remote peer, encrypted by local peer's public key
    /// </summary>
    class EncryptedP2pStreamParameters
    {
        byte[] destinationEndpointEncrypted; // IP address of A + UDP port + salt 
        byte[] P2pStreamIdEncrypted; // encrypted ushort
        byte[] KeyForHmacEncrypted;
    }

    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->RP->M->N
    /// пиры помнят путь по RequestID  пиры уже авторизовали друг друга на этом этапе
    /// </summary>
    class RegisterAckPacket
    {
        byte ReservedFlagsMustBeZero;
        RegistrationPublicKey RequesterPublicKey_RequestID;
        EncryptedP2pStreamParameters  RequesterEndoint_encryptedByNeighborPublicKey; // IP address of A + UDP port + salt // initiall IP address of A comes from RP  // possible attacks by RP???
        byte[] RequesterSignature; // is verified by N; MAY be verified by  RP, N

        HMAC SenderHMAC; // is NULL for A->RP
    }

    /// <summary>
    /// is sent by A when it receives signed ping from N
    /// A->RP->M
    /// пиры уже авторизовали друг друга на этом этапе
    /// пиры финализуруют состояние, обновляют рейтинг (всех по цепочке)
    /// </summary>
    class RegisterConfirmedPacket
    {
        byte ReservedFlagsMustBeZero;
        RegistrationPublicKey RequesterPublicKey_RequestID;
        byte succeeded; // 1 bit, to make the signature different from initial "SYN" part
        byte[] RequesterSignature; // is verified by N, RP,M  before updating rating

        HMAC SenderHMAC; // is NULL for A->RP
    }


    enum DrpResponderStatusCode
    {
        confirmed,

        rejected_badSenderRating,
        rejected_badtimestamp,
        rejected_maxhopsReached,
        rejected_noGoodPeers, // timed out or dead end in IDspace
        rejected_userBusyForInvite
    }
    class PingPacket
    {
        byte ReservedFlagsMustBeZero;
        uint Timestamp;
        float MaxRxInviteRateRps;
        float MaxRxRegisterRateRps;
        HMAC SenderHMAC;
    }
    /// <summary>
    /// A=requester
    /// B=responder
    /// A->N->X->B1
    /// </summary>
    class InviteRequestPacket
    {
        // requestID={RequesterPublicKey|DestinationResponderPublicKey}

        byte[] DirectChannelEndointA_encryptedByResponderPublicKey; // with salt // can be decrypted only by B
        /// <summary>
        /// todo: look at noise protocol
        /// todo: look at ECIES
        /// </summary>
        byte[] DirectChannelSecretBA_encryptedByResponderPublicKey;
        byte[] RequesterMessage_encryptedByResponderPublicKey; // messenger top-level protocol
        RegistrationPublicKey RequesterPublicKey; // A public key 
        RegistrationPublicKey DestinationResponderPublicKey; // B public key
        byte[] RequesterSignature;

        byte NumberOfHopsRemaining; // max 10 // is decremented by peers

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        HMAC SenderHmac;
    }
    /// <summary>
    /// B1->X->N->A (rejected/confirmed)
    /// </summary>
    class InviteResponsePacket
    {
        // requestID={RequesterPublicKey|DestinationResponderPublicKey}

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
        HMAC SenderHmac;
    }
}
