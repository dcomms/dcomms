using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// A=requester
    /// B=responder
    /// A->N->X->B1
    /// </summary>
    public class InviteRequestPacket
    {
        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public P2pConnectionToken32 SenderToken32;
        // byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public uint Timestamp32S;
        public RegistrationId RequesterPublicKey; // A public key 
        public RegistrationId ResponderPublicKey; // B public key
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new DirectChannel connection
        public RegistrationSignature RequesterSignature;

        public byte NumberOfHopsRemaining; // max 10 // is decremented by peers

        public NeighborPeerAckSequenceNumber16 NpaSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC SenderHMAC;

        public byte[] Encode_SetP2pFields(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteSyn);

            NpaSeq16 = transmitToNeighbor.GetNewNhaSeq16_P2P();
            SenderToken32 = transmitToNeighbor.RemotePeerToken32;
            SenderToken32.Encode(w);

            byte flags = 0;
            w.Write(flags);
            GetSignedFieldsForSenderHMAC(w);

            SenderHMAC = transmitToNeighbor.GetSenderHMAC(GetSignedFieldsForSenderHMAC);
            SenderHMAC.Encode(w);

            return ms.ToArray();
        }
        internal void GetSignedFieldsForSenderHMAC(BinaryWriter w)
        {
            GetSharedSignedFields(w);
            RequesterSignature.Encode(w);
            w.Write(NumberOfHopsRemaining);
            NpaSeq16.Encode(w);
        }
        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(Timestamp32S);
            RequesterPublicKey.Encode(w);
            ResponderPublicKey.Encode(w);
            RequesterEcdhePublicKey.Encode(w);
        }

        internal byte[] DecodedUdpPayloadData;
        public static InviteRequestPacket Decode_VerifySenderHMAC(byte[] udpPayloadData, ConnectionToNeighbor receivedFromNeighbor)
        {
            var r = new InviteRequestPacket();
            r.DecodedUdpPayloadData = udpPayloadData;
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            r.SenderToken32 = P2pConnectionToken32.Decode(reader);
            if (receivedFromNeighbor.LocalRxToken32.Equals(r.SenderToken32) == false)
                throw new UnmatchedFieldsException();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.Timestamp32S = reader.ReadUInt32();
            r.RequesterPublicKey = RegistrationId.Decode(reader);
            r.ResponderPublicKey = RegistrationId.Decode(reader);
            r.RequesterEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.RequesterSignature = RegistrationSignature.Decode(reader);
            r.NumberOfHopsRemaining = reader.ReadByte();
            r.NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);

            r.SenderHMAC = HMAC.Decode(reader);
            if (r.SenderHMAC.Equals(receivedFromNeighbor.GetSenderHMAC(r.GetSignedFieldsForSenderHMAC)) == false)
                throw new BadSignatureException();

            return r;
        }
        public void GetUniqueRequestIdFields(BinaryWriter writer)
        {
            RequesterPublicKey.Encode(writer);
            ResponderPublicKey.Encode(writer);
            writer.Write(Timestamp32S);
        }
    }
}
