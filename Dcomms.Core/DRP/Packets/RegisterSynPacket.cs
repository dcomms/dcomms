using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// REGISTER SYN request, is sent from A to RP
    /// is sent from RP to M, from M to N
    /// is sent over established P2P UDP channels that are kept alive by pings.  
    /// proxy sender peer is authenticated by source IP:UDP port and SenderHMAC
    /// </summary>
    public class RegisterSynPacket
    {
        /// <summary>
        /// 1: if packet is transmitted from registering A to RP, 
        /// 0: if packet is transmitted between neighbor peers (from sender to receiver). SenderHMAC is sent 
        /// </summary>
        static byte Flag_AtoRP = 0x01;

        public bool AtoRP => SenderToken32 == null;
        public P2pConnectionToken32 SenderToken32; // is not transmitted in A->RP request

        public RegistrationPublicKey RequesterPublicKey_RequestID; // used to verify signature // used also as request ID
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new P2P connection
                                       

        /// <summary>
        /// against flood by this packet in future, without A (against replay attack)
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint Timestamp32S;

        public uint MinimalDistanceToNeighbor; // is set to non-zero when requester wants to expand neighborhood 

        /// <summary>
        /// signs fields: {RequesterPublicKey_RequestID,RequesterEcdhePublicKey,Timestamp32S,MinimalDistanceToNeighbor}
        /// the signature is needed: 
        /// 1 to authorize sender of the request when intermediate peers build RDRs and rating of sender
        /// 2 to authorize sender at neighbor, to reject blacklisted requesters and prioritize previously known good requester neighbors
        /// is verified by neighbor (N), MAY be verified by proxy peers
        /// </summary>
        public RegistrationSignature RequesterSignature;
     


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
        public NextHopAckSequenceNumber16 NhaSeq16;
        public IPEndPoint RpEndpoint; // is transmitted only in A->RP request, unencrypted  // makes sense when RP is behind NAT (e.g amazon) and does not know its public IP

        public RegisterSynPacket()
        {
        }
        /// <param name="txParametersToPeerNeighbor">is not null for packets between registered peers</param>
        public byte[] Encode(P2pStreamParameters txParametersToPeerNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterSynPacket);
            byte flags = 0;
            if (txParametersToPeerNeighbor != null) flags |= Flag_AtoRP;
            writer.Write(flags);
            if (txParametersToPeerNeighbor != null)
                txParametersToPeerNeighbor.RemotePeerToken32.Encode(writer);

            GetCommonRequesterAndResponderFields(writer, true);

            if (txParametersToPeerNeighbor == null)
            {
                if (ProofOfWork2.Length != 64) throw new ArgumentException();
                writer.Write(ProofOfWork2);
            }
            writer.Write(NumberOfHopsRemaining);
            if (txParametersToPeerNeighbor != null)
            {
                throw new NotImplementedException();
             //   txParametersToPeerNeighbor.GetSharedHmac(cryptoLibrary, this.GetFieldsForSenderHmac).Encode(writer);
            }
            NhaSeq16.Encode(writer);
            if (txParametersToPeerNeighbor == null)
                PacketProcedures.EncodeIPEndPoint(writer, RpEndpoint);

            return ms.ToArray();
        }
        /// <summary>
        /// used for signature at requester; as source AEAD hash
        /// </summary>
        public void GetCommonRequesterAndResponderFields(BinaryWriter writer, bool includeRequesterSignature)
        {
            RequesterPublicKey_RequestID.Encode(writer);
            RequesterEcdhePublicKey.Encode(writer);
            writer.Write(Timestamp32S);
            writer.Write(MinimalDistanceToNeighbor);
            if (includeRequesterSignature) RequesterSignature.Encode(writer);
        }

        public RegisterSynPacket(byte[] udpPayloadData)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            var flags = reader.ReadByte();
            if ((flags & Flag_AtoRP) == 0) SenderToken32 = P2pConnectionToken32.Decode(reader);

            RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            RequesterEcdhePublicKey = EcdhPublicKey.Decode(reader);
            Timestamp32S = reader.ReadUInt32();
            MinimalDistanceToNeighbor = reader.ReadUInt32();
            RequesterSignature = RegistrationSignature.Decode(reader);
            
            if ((flags & Flag_AtoRP) != 0) ProofOfWork2 = reader.ReadBytes(64);
            NumberOfHopsRemaining = reader.ReadByte();
            if ((flags & Flag_AtoRP) == 0) SenderHMAC = HMAC.Decode(reader);
            if ((flags & Flag_AtoRP) != 0) RpEndpoint = PacketProcedures.DecodeIPEndPointIpv4(reader);

            NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
        }
        public static bool IsAtoRP(byte[] udpPayloadData)
        {
            var flags = udpPayloadData[1];
           return (flags & Flag_AtoRP) != 0;
        }
    }
}
