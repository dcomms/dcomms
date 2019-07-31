using Dcomms.Cryptography;
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
        NextHopResponsePacket = 4,
        RegisterSynAckPacket = 5,
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
    /// REGISTER SYN request, is sent from A to RP
    /// is sent from RP to M, from M to N
    /// is sent over established P2P UDP channels that are kept alive by pings.  
    /// proxy sender peer is authenticated by source IP:UDP port and SenderHMAC
    /// </summary>
    class RegisterSynPacket
    {
        public static byte Flag_AtoRP = 0x01; // set if packet is transmitted from registering A to RP, otherwise it is zero
        
        public RemotePeerToken16 SenderToken16; // is not transmitted in A->RP request
 
        public RegistrationPublicKey RequesterPublicKey_RequestID; // used to verify signature // used also as request ID
        /// <summary>
        /// against flood by this packet in future, without A (against replay attack)
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint Timestamp32S;

        public byte MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood // 32-byte xor distance compressed into 8 bits (log2)
        public RegistrationSignature RequesterSignature; // {RequesterPublicKey_RequestID,Timestamp32S,MinimalDistanceToNeighbor} // is verified by N, MAY be verified by proxies

        /// <summary>
        /// is transmitted only from A to RP
        /// sha512(RequesterPublicKey_RequestID|ProofOrWork2Request|ProofOfWork2) has byte[6]=7    
        /// 64 bytes
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
        public HMAC SenderHMAC;

        public RegisterSynPacket()
        {
        }
        public byte[] Encode(byte flags)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterSynPacket);
            writer.Write(flags);
            if ((flags & Flag_AtoRP) == 0) SenderToken16.Encode(writer);
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(Timestamp32S);
            writer.Write(MinimalDistanceToNeighbor);
            RequesterSignature.Encode(writer);
            if ((flags & Flag_AtoRP) != 0)
            {
                if (ProofOfWork2.Length != 64) throw new ArgumentException();
                writer.Write(ProofOfWork2);
            }
            writer.Write(NumberOfHopsRemaining);
            if ((flags & Flag_AtoRP) == 0) SenderHMAC.Encode(writer);
                      
            return ms.ToArray();
        }
        /// <param name="reader">is positioned after first byte = packet type</param>
        public RegisterSynPacket(BinaryReader reader)
        {
            var flags = reader.ReadByte();
            if ((flags & Flag_AtoRP) == 0) SenderToken16 = RemotePeerToken16.Decode(reader);
            RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            Timestamp32S = reader.ReadUInt32();
            MinimalDistanceToNeighbor = reader.ReadByte();
            RequesterSignature = RegistrationSignature.Decode(reader);
            if ((flags & Flag_AtoRP) != 0) ProofOfWork2 = reader.ReadBytes(64);
            NumberOfHopsRemaining = reader.ReadByte();
            if ((flags & Flag_AtoRP) == 0) SenderHMAC = HMAC.Decode(reader);
        }
    }
    /// <summary>
    /// is sent from next hop to previous hop, when the next hop receives some packet from neighbor, or from registering peer (RP->A). stops UDP retransmission of a request packet
    /// </summary>
    class NextHopResponsePacket
    {
        public static byte Flag_RPtoA = 0x01; // set if packet is transmitted from RP to A, is zero otherwise

        public RemotePeerToken16 SenderToken16; // is not transmitted in RP->A packet
        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public NextHopResponseCode StatusCode;
        /// <summary>
        /// signature of sender neighbor peer
        /// is NULL for RP->A packet
        /// uses common secret of neighbors within P2P connection
        /// </summary>
        public HMAC SenderHMAC;
        
        public byte[] Encode(byte flags)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.NextHopResponsePacket);
            writer.Write(flags);
            if ((flags & Flag_RPtoA) == 0) SenderToken16.Encode(writer);
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write((byte)StatusCode);          
            if ((flags & Flag_RPtoA) == 0) SenderHMAC.Encode(writer);            
            return ms.ToArray();
        }
        /// <param name="reader">is positioned after first byte = packet type</param>
        public NextHopResponsePacket(BinaryReader reader)
        {
            var flags = reader.ReadByte();
            if ((flags & Flag_RPtoA) == 0) SenderToken16 = RemotePeerToken16.Decode(reader);
            RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            StatusCode = (NextHopResponseCode)reader.ReadByte();           
            if ((flags & Flag_RPtoA) == 0) SenderHMAC = HMAC.Decode(reader);
        }
    }
    enum NextHopResponseCode
    {
        received, // is sent to previous hop immediately when packet is proxied, to avoid retransmissions      
        rejected_overloaded,
        rejected_rateExceeded, // anti-ddos
    }
    
    /// <summary>
    /// response to REGISTER SYN request, 
    /// is sent from neighbor=responder=N to M, from M to RP, from RP to A
    /// ответ от N к A идет по тому же пути, узлы помнят обратный путь по RequestId
    /// </summary>
    class RegisterSynAckPacket
    {
        public static byte Flag_RPtoA = 0x01; // set if packet is transmitted from RP to registering A, otherwise it is zero
             
        RemotePeerToken16 SenderToken16; // is not sent from RP to A
        DrpResponderStatusCode NeighborStatusCode;
        /// <summary>
        /// not null only for (status=connected) (N->X-M-RP-A)
        /// IP address of N with salt, encrypted for A
        /// </summary>
        EncryptedP2pStreamTxParameters NeighborEndpoint_EncryptedByRequesterPublicKey;
       
        RegistrationPublicKey RequesterPublicKey_RequestID; // public key of requester (A)
        /// <summary>
        /// against flood by this packet in future, without N (against replay attack)
        /// is copied from REISTER SYN request packet by N and put into the SYN-ACK response
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint RegisterSynTimestamp32S;

        RegistrationPublicKey NeighborPublicKey; // public key of responder (neighbor, N)
        RegistrationSignature NeighborSignature; // {NeighborStatusCode,NeighborEndpoint_EncryptedByRequesterPublicKey,RequesterPublicKey_RequestID,RegisterSynTimestamp32S,NeighborPublicKey }

        HMAC SenderHMAC; // is not sent from RP to A
        IPEndPoint RequesterEndpoint; // is sent only from RP to A to provide public IP:port of A

        /// <summary>
        /// decodes the packet, decrypts NeighborEndpoint_EncryptedByRequesterPublicKey, verifies NeighborSignature
        /// </summary>
        /// <param name="reader">is positioned after first byte = packet type</param>
        /// <param name="requesterPublicKey">is used to decrypt NeighborEndpoint_EncryptedByRequesterPublicKey</param>
        public RegisterSynAckPacket(BinaryReader reader, RegistrationPublicKey requesterPublicKey)
        {
            var flags = reader.ReadByte();
            if ((flags & Flag_RPtoA) == 0) SenderToken16 = RemotePeerToken16.Decode(reader);

            NeighborStatusCode = (DrpResponderStatusCode)reader.ReadByte();
            NeighborEndpoint_EncryptedByRequesterPublicKey = EncryptedP2pStreamTxParameters.Decode(reader, requesterPublicKey);

            xx

            if ((flags & Flag_RPtoA) == 0)
                SenderHMAC = HMAC.Decode(reader);
        }
    }

    /// <summary>
    /// is generated by remote peer; token of local peer at remote peer;
    /// put into every p2p packet,
    /// is needed  1) for faster lookup of remote peer by 16 bits 2) to have multiple DRP peer reg IDs running at same UDP port
    /// is unique at remote (responder) peer; is used to identify local (sender) peer at remote peer (together with HMAC)
    /// </summary>
    class RemotePeerToken16
    {
        public ushort Token16;
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Token16);
        }
        public static RemotePeerToken16 Decode(BinaryReader reader)
        {
            var r = new RemotePeerToken16();
            r.Token16 = reader.ReadUInt16();
            return r;
        }
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
        /// <summary>
        /// decrypts packet
        /// </summary>
        public EncryptedP2pStreamTxParameters(BinaryReader reader, RegistrationPrivateKey privateKey, ICryptoLibrary cryptoLibrary)
        {
            reader.ReadBytes();
            cryptoLibrary.DecryptEd25519();
        }

        public void Encode(BinaryWriter writer, RegistrationPublicKey publicKey, ICryptoLibrary cryptoLibrary)
        {
            writer.Write();
        }
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
