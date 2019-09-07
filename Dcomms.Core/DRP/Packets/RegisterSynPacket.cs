using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// REGISTER SYN request, is sent from A to EP
    /// is sent from EP to M, from M to N
    /// is sent over established P2P UDP channels that are kept alive by pings.  
    /// proxy sender peer is authenticated by source IP:UDP port and SenderHMAC
    /// </summary>
    public class RegisterSynPacket
    {
        /// <summary>
        /// 1: if packet is transmitted from registering A to EP, 
        /// 0: if packet is transmitted between neighbor peers (from sender to receiver). SenderHMAC is sent 
        /// </summary>
        static byte Flag_AtoEP = 0x01;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public bool AtoEP => SenderToken32 == null;

        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemotePeerToken32 in case when this packet goes over established P2P connection (flag A-EP is zero)
        /// </summary>
        public P2pConnectionToken32 SenderToken32;

        public RegistrationPublicKey RequesterPublicKey_RequestID; // used to verify signature // used also as request ID
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new P2P connection
                                       

        /// <summary>
        /// against flood by this packet in future, without A (against replay attack)
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint Timestamp32S;

        public uint MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood 
        public IPEndPoint EpEndpoint; // unencrypted  // makes sense when EP is behind NAT (e.g amazon) and does not know its public IP

        /// <summary>
        /// signs fields: {RequesterPublicKey_RequestID,RequesterEcdhePublicKey,Timestamp32S,MinimalDistanceToNeighbor,EpEndpoint}
        /// the signature is needed: 
        /// 1 to authorize sender of the request when intermediate peers build RDRs and rating of sender
        /// 2 to authorize sender at neighbor, to reject blacklisted requesters and prioritize previously known good requester neighbors
        /// is verified by neighbor (N), MAY be verified by proxy peers
        /// </summary>
        public RegistrationSignature RequesterSignature;
     
        /// <summary>
        /// is transmitted only from A to EP
        /// sha512(RequesterPublicKey_RequestID|ProofOrWork2Request|ProofOfWork2) has byte[6]=7    
        /// 64 bytes
        /// </summary>
        public byte[] ProofOfWork2;
        /// <summary>
        /// EP knows size of network.  he knows distance EP-A.  he knows "average number of hops" for this distance
        /// EP limits this field by "average number of hops"
        /// is decremented by peers
        /// </summary>
        public byte NumberOfHopsRemaining;

        public NextHopAckSequenceNumber16 NhaSeq16;
        /// <summary>
        /// signature of latest proxy sender: EP,M,X
        /// is NULL for A->EP packet
        /// uses common secret of neighbors within p2p connection
        /// </summary>
        public HMAC SenderHMAC;

        public RegisterSynPacket()
        {
        }
        /// <param name="connectionToNeighborNullable">is not null for packets between registered peers. if set, the procedure sets SenderToken32 and SenderHMAC</param>
        public byte[] Encode_OptionallySignSenderHMAC(ConnectionToNeighbor connectionToNeighborNullable)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterSyn);
            byte flags = 0;
            if (connectionToNeighborNullable == null) flags |= Flag_AtoEP;
            writer.Write(flags);
            if (connectionToNeighborNullable != null)
            {
                SenderToken32 = connectionToNeighborNullable.RemotePeerToken32;
                SenderToken32.Encode(writer);
            }

            GetCommonRequesterProxyResponderFields(writer, true);

            if (connectionToNeighborNullable == null)
            {
                if (ProofOfWork2.Length != 64) throw new ArgumentException();
                writer.Write(ProofOfWork2);
            }
            writer.Write(NumberOfHopsRemaining);
            NhaSeq16.Encode(writer);
            if (connectionToNeighborNullable != null)
            {
                SenderHMAC = connectionToNeighborNullable.GetSenderHMAC(this.GetSignedFieldsForSenderHMAC);
                SenderHMAC.Encode(writer);
            }

            return ms.ToArray();
        }
        /// <summary>
        /// used for signature at requester; as source AEAD hash
        /// </summary>
        public void GetCommonRequesterProxyResponderFields(BinaryWriter writer, bool includeRequesterSignature)
        {
            RequesterPublicKey_RequestID.Encode(writer);
            RequesterEcdhePublicKey.Encode(writer);
            writer.Write(Timestamp32S);
            writer.Write(MinimalDistanceToNeighbor);
            PacketProcedures.EncodeIPEndPoint(writer, EpEndpoint);
            if (includeRequesterSignature) RequesterSignature.Encode(writer);
        }
        internal void GetSignedFieldsForSenderHMAC(BinaryWriter writer)
        {
            SenderToken32.Encode(writer);
            GetCommonRequesterProxyResponderFields(writer, true);
            writer.Write(NumberOfHopsRemaining);
            NhaSeq16.Encode(writer);
        }
        public byte[] OriginalUdpPayloadData;

        /// <summary>
        /// when SYN is received from neighbor, verifies senderHMAC and SenderToken32
        /// </summary>
        /// <param name="receivedFromNeighborNullable">is NULL when decoding SYN from A at EP</param>
        public static RegisterSynPacket Decode_OptionallyVerifySenderHMAC(byte[] udpPayloadData, ConnectionToNeighbor receivedFromNeighborNullable)
        {
            var r = new RegisterSynPacket();
            r.OriginalUdpPayloadData = udpPayloadData;
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();
            if ((flags & Flag_AtoEP) == 0)
            {
                if (receivedFromNeighborNullable == null) throw new UnmatchedFieldsException();
                r.SenderToken32 = P2pConnectionToken32.Decode(reader);                
                if (receivedFromNeighborNullable.LocalRxToken32.Equals(r.SenderToken32) == false)
                    throw new UnmatchedFieldsException();
            }
            else
            {
                if (receivedFromNeighborNullable != null) throw new UnmatchedFieldsException();
            }

            r.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            r.RequesterEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.Timestamp32S = reader.ReadUInt32();
            r.MinimalDistanceToNeighbor = reader.ReadUInt32();
            r.EpEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
            r.RequesterSignature = RegistrationSignature.Decode(reader);
            
            if ((flags & Flag_AtoEP) != 0) r.ProofOfWork2 = reader.ReadBytes(64);
            r.NumberOfHopsRemaining = reader.ReadByte();
            r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
            if ((flags & Flag_AtoEP) == 0)
            {
                r.SenderHMAC = HMAC.Decode(reader);
                if (r.SenderHMAC.Equals(receivedFromNeighborNullable.GetSenderHMAC(r.GetSignedFieldsForSenderHMAC)) == false)
                    throw new BadSignatureException();
            }

            return r;
        }
      
        public static bool IsAtoEP(byte[] udpPayloadData)
        {
            var flags = udpPayloadData[1];
           return (flags & Flag_AtoEP) != 0;
        }
        
        public static ushort DecodeToken16FromUdpPayloadData_P2Pmode(byte[] udpPayloadData)
        { // first 2 bytes ares packet type and flags. then 4 bytes are P2pConnectionToken32
            return (ushort)(udpPayloadData[2] | (udpPayloadData[3] << 8));
        }
    }
}
