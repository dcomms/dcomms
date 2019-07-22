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
        // "initial SYN" part
        byte[] RequesterPublicKey; // used to verify signature
        /// <summary>
        /// against flood by this packet in future, without A
        /// </summary>
        uint Timestamp;

        ////  byte[] RequestId;// can not be changed by proxy peers
        ////  RequestID=sha32XXX(RequesterPublicKey)

        uint MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood
        byte[] RequesterSignature; // is verified by N, MAY be verified by proxies

        /// <summary>
        /// is transmitted only from A to RP
        /// sha512(RequestID|ProofOrWork2Request|ProofOfWork1) has byte[6]=7  
        /// includes powType 
        /// todo: argon2 cpuPoW
        /// </summary>
        byte[] ProofOfWork2;
        /// <summary>
        /// RP knows size of network.  he knows distance RP-A.  he knows avg n hops
        /// RP limits this field
        /// </summary>
        byte NumberOfHops;

        /// <summary>
        /// optional signature of latest proxy sender: RP,M,X
        /// optional: is set when next (receiver) hop requires the signature by sending reply with status=signatureRequired,
        /// when the next hop suspects IP-spoofed attack
        /// </summary>
        byte[] PreviousHopSignature;
    }
    /// <summary>
    /// is sent by next hop to previous hop
    /// possible DRP-level attack: sniffing RequestId + flooding with spoofed source IP
    /// </summary>
    class DrpNextHopResponsePacket
    {
        byte ReservedFlagsMustBeZero;
        byte[] RequestId;
        DrpNextHopResponseCode StatusCode;
    }
    enum DrpNextHopResponseCode
    {
        received, // is sent to previous hop immediately when packet is proxied, to avoid retransmissions
        signatureRequired,
        rejected_overloaded,
        rejected_badRequestId,
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
        byte[] NeighborEndpoint_EncryptedByRequesterPublicKey; 

        /// <summary>
        /// authorizes sender of this packet, together with source IP+ UDP port
        /// </summary>
        byte[] RequestId;
        byte[] NeighborPublicKey; // pub key of RP, M, N
        byte[] NeighborSignature; // signature of entire packet

        /// <summary>
        /// optional: is set when next hop requires the signature by sending reply with status=signatureRequired,
        /// when the next hop suspects IP-spoofed attack
        /// </summary>
        byte[] PreviousHopSignature;

        IPEndPoint endpointA; // is sent only from RP to A to provide public IP:port

    }

    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->RP->M->N
    /// пиры помнят путь по RequestID  пиры уже авторизовали друг друга на этом этапе
    /// </summary>
    class RegisterAckPacket
    {
        byte ReservedFlagsMustBeZero;
        byte[] RequestId;
        byte[] RequesterEndoint_encryptedByPubN; // IP address of A + UDP port + salt
        byte[] RequesterSignature; // is verified by N; MAY be verified by  RP, N

        /// <summary>
        /// optional: is set when next hop requires the signature by sending reply with status=signatureRequired,
        /// when the next hop suspects IP-spoofed attack
        /// </summary>
        byte[] PreviousHopSignature;
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
        byte[] RequestId;
        byte succeeded; // 1 bit, to make the signature different from initial "SYN" part
        byte[] RequesterSignature; // is verified by N, RP,M  before updating rating

        /// <summary>
        /// optional, is set when next hop requires the signature (in case when next hop suspects IP-spoofed attack)
        /// подпись последнего прокси-отправителя RP,M,X 
        /// </summary>
        byte[] SenderSignature;
    }


    enum DrpResponderStatusCode
    {
        confirmed,

        rejected, // no neighbors
        rejected_badtimestamp,
        rejected_maxhopsReached,
        rejected_userBusyForInvite
    }
    class PingPacket
    {
        byte Flags; // bit0 = "bad time"  // bit1="require signature"   // attack on neighbor: fake "bad time": max time sync per minute
        uint Timestamp;

        /// <summary>
        /// if wrong hmac:  don't reply
        /// 
        /// </summary>
        byte[] hmac;


        /// <summary>
        /// is sent by requester if another party suspects ip-spoofed DoS and requires signature
        /// </summary>
        byte[] SenderSignature;
    }
    /// <summary>
    /// A=requester
    /// B=responder
    /// A->N->X->B1
    /// </summary>
    class InviteRequestPacket
    {
        // requestID={publicKeyA|publicKeyB}

      //  byte[] RequestId; // set by A=requester, can not be changed by hops // is valid within p2p connection // min 4 bytes
        byte[] DirectChannelEndointA_encryptedByResponderPublicKey; // with salt // can be decrypted only by B
        /// <summary>
        /// todo: look at noise protocol
        /// todo: look at ECIES
        /// </summary>
        byte[] DirectChannelSecretBA_encryptedByResponderPublicKey;
        byte[] SourceRequesterPublicKey; // A public key 
        byte[] DestinationResponderPublicKey; // B public key
        byte[] RequesterSignature;

        byte NumberOfHopsRemaining; // max 10 // is decremented by peers

        /// <summary>
        /// optional, is set when next hop requires the signature (in case when next hop suspects IP-spoofed DoS attack)
        /// подпись последнего прокси-отправителя RP,M,X 
        /// </summary>
     //   byte[] SenderSignature;

        byte[] SenderHmac;
    }
    /// <summary>
    /// B1->X->N->A (rejected/confirmed)
    /// </summary>
    class InviteResponsePacket
    {
        byte[] RequestId;
        DrpResponderStatusCode StatusCode;
        byte[] DirectChannelEndointB_encryptedByRequesterPublicKey; // with salt // user key or reg key?    
        byte[] DirectChannelSecretAB_encryptedByRequesterPublicKey;
        byte[] ResponderSignature;
    }
}
