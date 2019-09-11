using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{
    class InviteSynAckPacket
    {

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public P2pConnectionToken32 SenderToken32;
        // byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public uint Timestamp32S;
        public RegistrationPublicKey RequesterPublicKey; // A public key 
        public RegistrationPublicKey ResponderPublicKey; // B public key
        public EcdhPublicKey ResponderEcdhePublicKey;
        public byte[] ToResponderSessionDescriptionEncrypted;
        public RegistrationSignature ResponderSignature;

        public NextHopAckSequenceNumber16 NhaSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC SenderHMAC;

        public byte[] Encode(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteSynAck);

            SenderToken32 = transmitToNeighbor.RemotePeerToken32;
            SenderToken32.Encode(w);

            byte flags = 0;
            w.Write(flags);
            GetSignedFieldsForSenderHMAC(w);

            SenderHMAC = transmitToNeighbor.GetSenderHMAC(GetSignedFieldsForSenderHMAC);
            SenderHMAC.Encode(w);

            return ms.ToArray();
        }
        void GetSignedFieldsForSenderHMAC(System.IO.BinaryWriter w)
        {
            w.Write(Timestamp32S);
            RequesterPublicKey.Encode(w);
            ResponderPublicKey.Encode(w);
            ResponderEcdhePublicKey.Encode(w);
            PacketProcedures.EncodeByteArray65536(w, ToResponderSessionDescriptionEncrypted);
            ResponderSignature.Encode(w);
            NhaSeq16.Encode(w);
        }

        public static InviteSynAckPacket Decode_VerifySenderHMAC(byte[] udpPayloadData, ConnectionToNeighbor receivedFromNeighbor)
        {
            var r = new InviteSynAckPacket();
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            r.SenderToken32 = P2pConnectionToken32.Decode(reader);
            if (receivedFromNeighbor.LocalRxToken32.Equals(r.SenderToken32) == false)
                throw new UnmatchedFieldsException();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.Timestamp32S = reader.ReadUInt32();
            r.RequesterPublicKey = RegistrationPublicKey.Decode(reader);
            r.ResponderPublicKey = RegistrationPublicKey.Decode(reader);
            r.ResponderEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.ToResponderSessionDescriptionEncrypted = PacketProcedures.DecodeByteArray65536(reader);
            r.ResponderSignature = RegistrationSignature.Decode(reader);
            r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            r.SenderHMAC = HMAC.Decode(reader);
            if (r.SenderHMAC.Equals(receivedFromNeighbor.GetSenderHMAC(r.GetSignedFieldsForSenderHMAC)) == false)
                throw new BadSignatureException();

            return r;
        }

    }
}
