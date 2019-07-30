using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    enum DrpPacketType
    {
        RegisterPow1RequestPacket = 1,
        RegisterPow1ResponsePacket = 2,
        RegisterSynPacket = 3,
        DrpNextHopResponsePacket = 4,
    }

    /// <summary>
    /// A = requester
    /// RP = rendezvous server, proxy peer
    /// is sent from A to RP when A connects to the P2P network
    /// protects system against IP spoofing
    /// </summary>
    class RegisterPow1RequestPacket
    {
        public byte ReservedFlagsMustBeZero; // will include PoW type
        public uint Timestamp32S; // seconds since 2019-01-01 UTC, 32 bits are enough for 136 years

        /// <summary>
        /// default PoW type: 64 bytes
        /// sha512(Timestamp32S|ProofOfWork1|requesterPublicIp) has byte[6]=7 
        /// todo: consider PoW's based on argon2, bcrypt, scrypt:  slow on GPUs.   the SHA512 is fast on GPUs, that could be used by DDoS attackers
        /// </summary>
        public byte[] ProofOfWork1;     

        public RegisterPow1RequestPacket()
        {
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write((byte)DrpPacketType.RegisterPow1RequestPacket);
            writer.Write(ReservedFlagsMustBeZero);
            writer.Write(Timestamp32S);
            if (ProofOfWork1.Length != 64) throw new ArgumentException();
            writer.Write(ProofOfWork1);
        }
        public readonly byte[] OriginalPacketPayload;

        /// <param name="reader">positioned after first byte = packet type</param>
        public RegisterPow1RequestPacket(BinaryReader reader, byte[] originalPacketPayload)
        {
            OriginalPacketPayload = originalPacketPayload;
            ReservedFlagsMustBeZero = reader.ReadByte();
            Timestamp32S = reader.ReadUInt32();
            ProofOfWork1 = reader.ReadBytes(64);
        }
    }
    /// <summary>
    /// verifies UDP/IP path from RP to A, check UDP.sourceIP
    /// can be used for UDP reflection attacks
    /// </summary>
    class RegisterPow1ResponsePacket
    {
        public byte ReservedFlagsMustBeZero;
        public RegisterPowResponseStatusCode StatusCode;
        public byte[] ProofOfWork2Request; // 16 bytes
        
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpPacketType.RegisterPow1ResponsePacket);
            writer.Write(ReservedFlagsMustBeZero);
            writer.Write((byte)StatusCode);
            if (StatusCode == RegisterPowResponseStatusCode.succeeded_Pow2Challenge)
                writer.Write(ProofOfWork2Request);
            return ms.ToArray();
        }
        /// <param name="reader">is positioned after first byte = packet type</param>
        public RegisterPow1ResponsePacket(BinaryReader reader) 
        {
            ReservedFlagsMustBeZero = reader.ReadByte();
            StatusCode = (RegisterPowResponseStatusCode)reader.ReadByte();
            if (StatusCode == RegisterPowResponseStatusCode.succeeded_Pow2Challenge)
            {
                ProofOfWork2Request = reader.ReadBytes(16);
            }
        }
    }
    enum RegisterPowResponseStatusCode
    {
        succeeded_Pow2Challenge,
                
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
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
 
        public RegistrationPublicKey RequesterPublicKey_RequestID; // used to verify signature // used also as request ID
        /// <summary>
        /// against flood by this packet in future, without A (against replay attack)
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint Timestamp32S;

        public uint MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood
        public byte[] RequesterSignature; // {RequesterPublicKey_RequestID,Timestamp32S,MinimalDistanceToNeighbor} // is verified by N, MAY be verified by proxies

        /// <summary>
        /// is transmitted only from A to RP
        /// sha512(RequesterPublicKey_RequestID|ProofOrWork2Request|ProofOfWork2) has byte[6]=7       
        /// </summary>
        public byte[] ProofOfWork2;
        /// <summary>
        /// RP knows size of network.  he knows distance RP-A.  he knows "average number of hops" for this distance
        /// RP limits this field by "average number of hops"
        /// is decremented by peers
        /// </summary>
        public byte NumberOfHopsRemaining;

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
        RemotePeerToken16 SenderToken16;
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
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
        DrpResponderStatusCode NeighborStatusCode;
        /// <summary>
        /// not null only for (status=connected) (N->X-M-RP-A)
        /// IP address of N with salt, encrypted for A
        /// </summary>
        EncryptedP2pStreamTxParameters NeighborEndpoint_EncryptedByRequesterPublicKey;
       

        RegistrationPublicKey RequesterPublicKey_RequestID;
        RegistrationPublicKey NeighborPublicKey; // pub key of RP, M, N
        byte[] NeighborSignature; // signature of entire packet

        HMAC SenderHMAC; // is NULL for RP->A

        IPEndPoint RequesterEndpoint; // is sent only from RP to A to provide public IP:port
    }

    /// <summary>
    /// is generated by remote peer; token of local peer at remote peer;
    /// put into every p2p packet,
    /// is needed  1) for faster lookup of remote peer by 16 bits 2) to have multiple DRP peer reg IDs running at same UDP port
    /// is unique at remote (responder) peer; is used to identify local (sender) peer at remote peer (together with HMAC)
    /// </summary>
    class RemotePeerToken16
    {
        ushort Token16;
    }

    /// <summary>
    /// parameters to transmit DRP pings and proxied packets from local peer to remote peer
    /// sent from remote peer to local peer via REGISTER channel
    /// parameters to connect to remote peer, encrypted by local peer's public key
    /// </summary>
    class EncryptedP2pStreamTxParameters
    {
        // all following fields are encrypted with destination (remote) peer's public key
        IPEndPoint DestinationEndpoint; // IP address + UDP port + salt(?) 
        RemotePeerToken16 RemotePeerToken16;
        byte[] KeyForHMAC;
    }

    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->RP->M->N
    /// пиры помнят путь по RequestID  пиры уже авторизовали друг друга на этом этапе
    /// </summary>
    class RegisterAckPacket
    {
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
        RegistrationPublicKey RequesterPublicKey_RequestID;
        EncryptedP2pStreamTxParameters  RequesterEndoint_encryptedByNeighborPublicKey; // IP address of A + UDP port + salt // initiall IP address of A comes from RP  // possible attacks by RP???
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

        //todo add some signed data from N

        HMAC SenderHMAC; // is NULL for A->RP
    }


    public enum DrpResponderStatusCode
    {
        confirmed,

        rejected_badSenderRating,
        rejected_badtimestamp,
        rejected_maxhopsReached,
        rejected_noGoodPeers, // timed out or dead end in IDspace
        rejected_userBusyForInvite
    }
    class PingRequestPacket
    {
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
        ushort Timestamp; // msec
        float? MaxRxInviteRateRps;   // signal from sender "how much I can receive via this p2p connection"
        float? MaxRxRegisterRateRps; // signal from sender "how much I can receive via this p2p connection"
        HMAC SenderHMAC;
    }
    class PingResponsePacket
    {
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
        ushort TimestampOfRequest; // msec
        HMAC SenderHMAC;
    }
    /// <summary>
    /// A=requester
    /// B=responder
    /// A->N->X->B1
    /// </summary>
    class InviteRequestPacket
    {
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
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
        HMAC SenderHMAC;
    }
    /// <summary>
    /// B1->X->N->A (rejected/confirmed)
    /// </summary>
    class InviteResponsePacket
    {
        RemotePeerToken16 SenderToken16;
        byte ReservedFlagsMustBeZero;
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
        HMAC SenderHMAC;
    }
}
