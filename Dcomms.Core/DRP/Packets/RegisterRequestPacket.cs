using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// REGISTER REQ request, is sent from A to EP
    /// is sent from EP to M, from M to N
    /// is sent over established P2P UDP channels that are kept alive by pings.  
    /// proxy sender peer is authenticated by source IP:UDP port and NeighborHMAC
    /// </summary>
    public class RegisterRequestPacket
    {
        /// <summary>
        /// 1: if packet is transmitted from registering A to EP, 
        /// 0: if packet is transmitted between neighbor peers (from sender to receiver). NeighborHMAC is sent 
        /// </summary>
        static byte Flag_AtoEP = 0x01;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public bool AtoEP => NeighborToken32 == null;

        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32 in case when this packet goes over established P2P connection (flag A-EP is zero)
        /// </summary>
        public NeighborToken32 NeighborToken32;

        public RegistrationId RequesterRegistrationId; // used to verify signature // used also as request ID
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new P2P connection
                     
        /// <summary>
        /// against flood by this packet in future, without A (against replay attack)
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint ReqTimestamp32S;

        public uint MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood 
        public IPEndPoint EpEndpoint; // unencrypted  // makes sense when EP is behind NAT (e.g amazon) and does not know its public IP

        /// <summary>
        /// signs fields: {RequesterRegistrationId,RequesterEcdhePublicKey,Timestamp32S,MinimalDistanceToNeighbor,EpEndpoint}
        /// the signature is needed: 
        /// 1 to authorize sender of the request when intermediate peers build RDRs and rating of sender
        /// 2 to authorize sender at neighbor, to reject blacklisted requesters and prioritize previously known good requester neighbors
        /// is verified by neighbor (N), MAY be verified by proxy peers
        /// </summary>
        public RegistrationSignature RequesterSignature;
     
        /// <summary>
        /// is transmitted only from A to EP
        /// sha512(RequesterRegistrationId|ProofOrWork2Request|ProofOfWork2) has byte[6]=7    
        /// 64 bytes
        /// </summary>
        public byte[] ProofOfWork2;
        /// <summary>
        /// EP knows size of network.  he knows distance EP-A.  he knows "average number of hops" for this distance
        /// EP limits this field by "average number of hops"
        /// is decremented by peers
        /// </summary>
        public byte NumberOfHopsRemaining;

        public NeighborPeerAckSequenceNumber16 NpaSeq16;
        /// <summary>
        /// signature of latest proxy sender: EP,M,X
        /// is NULL for A->EP packet
        /// uses common secret of neighbors within p2p connection
        /// </summary>
        public HMAC NeighborHMAC;

        public RegisterRequestPacket()
        {
        }
        /// <param name="connectionToNeighborNullable">is not null for packets between registered peers. if set, the procedure sets NeighborToken32 and NeighborHMAC</param>
        public byte[] Encode_OptionallySignNeighborHMAC(ConnectionToNeighbor connectionToNeighborNullable)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpDmpPacketTypes.RegisterReq);
            byte flags = 0;
            if (connectionToNeighborNullable == null) flags |= Flag_AtoEP;
            writer.Write(flags);
            if (connectionToNeighborNullable != null)
            {
                NeighborToken32 = connectionToNeighborNullable.RemoteNeighborToken32;
                NeighborToken32.Encode(writer);
            }

            GetSharedSignedFields(writer, true);

            if (connectionToNeighborNullable == null)
            {
                if (ProofOfWork2.Length != 64) throw new ArgumentException();
                writer.Write(ProofOfWork2);
            }
            writer.Write(NumberOfHopsRemaining);
            NpaSeq16.Encode(writer);
            if (connectionToNeighborNullable != null)
            {
                NeighborHMAC = connectionToNeighborNullable.GetNeighborHMAC(this.GetSignedFieldsForNeighborHMAC);
                NeighborHMAC.Encode(writer);
            }

            return ms.ToArray();
        }
        /// <summary>
        /// used for signature at requester; as source AEAD hash
        /// </summary>
        public void GetSharedSignedFields(BinaryWriter writer, bool includeRequesterSignature)
        {
            RequesterRegistrationId.Encode(writer);
            RequesterEcdhePublicKey.Encode(writer);
            writer.Write(ReqTimestamp32S);
            writer.Write(MinimalDistanceToNeighbor);
            PacketProcedures.EncodeIPEndPoint(writer, EpEndpoint);
            if (includeRequesterSignature) RequesterSignature.Encode(writer);
        }
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter writer)
        {
            NeighborToken32.Encode(writer);
            GetSharedSignedFields(writer, true);
            writer.Write(NumberOfHopsRemaining);
            NpaSeq16.Encode(writer);
        }
        public byte[] DecodedUdpPayloadData;

        /// <summary>
        /// when REQ is received from neighbor, verifies senderHMAC and NeighborToken32
        /// </summary>
        /// <param name="receivedFromNeighborNullable">is NULL when decoding REQ from A at EP</param>
        public static RegisterRequestPacket Decode_OptionallyVerifyNeighborHMAC(byte[] udpPayloadData, ConnectionToNeighbor receivedFromNeighborNullable)
        {
            var r = new RegisterRequestPacket();
            r.DecodedUdpPayloadData = udpPayloadData;
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();
            if ((flags & Flag_AtoEP) == 0)
            {
                if (receivedFromNeighborNullable == null) throw new UnmatchedFieldsException();
                r.NeighborToken32 = NeighborToken32.Decode(reader);                
                if (receivedFromNeighborNullable.LocalNeighborToken32.Equals(r.NeighborToken32) == false)
                    throw new UnmatchedFieldsException();
            }
            else
            {
                if (receivedFromNeighborNullable != null) throw new UnmatchedFieldsException();
            }

            r.RequesterRegistrationId = RegistrationId.Decode(reader);
            r.RequesterEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.ReqTimestamp32S = reader.ReadUInt32();
            r.MinimalDistanceToNeighbor = reader.ReadUInt32();
            r.EpEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
            r.RequesterSignature = RegistrationSignature.Decode(reader);
            
            if ((flags & Flag_AtoEP) != 0) r.ProofOfWork2 = reader.ReadBytes(64);
            r.NumberOfHopsRemaining = reader.ReadByte();
            r.NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);
            if ((flags & Flag_AtoEP) == 0)
            {
                r.NeighborHMAC = HMAC.Decode(reader);
                if (r.NeighborHMAC.Equals(receivedFromNeighborNullable.GetNeighborHMAC(r.GetSignedFieldsForNeighborHMAC)) == false)
                    throw new BadSignatureException();
            }

            return r;
        }
      
        public static bool IsAtoEP(byte[] udpPayloadData)
        {
            var flags = udpPayloadData[1];
           return (flags & Flag_AtoEP) != 0;
        }
        
        public static ushort DecodeNeighborToken16(byte[] udpPayloadData)
        { // first 2 bytes ares packet type and flags. then 4 bytes are NeighborToken32
            return (ushort)(udpPayloadData[2] | (udpPayloadData[3] << 8));
        }

        public void GetUniqueRequestIdFields(BinaryWriter writer)
        {
            RequesterRegistrationId.Encode(writer);
            writer.Write(ReqTimestamp32S);
        }
    }
}
