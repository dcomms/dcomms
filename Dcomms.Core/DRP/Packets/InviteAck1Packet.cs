using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    class InviteAck1Packet
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

        public byte[] ToRequesterSessionDescriptionEncrypted;
        public RegistrationSignature RequesterSignature;
        
        public NextHopAckSequenceNumber16 NhaSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC SenderHMAC;

        public byte[] Encode_SetP2pFields(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteAck);

            NhaSeq16 = transmitToNeighbor.GetNewNhaSeq16_P2P();
            SenderToken32 = transmitToNeighbor.RemotePeerToken32;
            SenderToken32.Encode(w);

            byte flags = 0;
            w.Write(flags);
            GetSignedFieldsForSenderHMAC(w);

            SenderHMAC = transmitToNeighbor.GetSenderHMAC(GetSignedFieldsForSenderHMAC);
            SenderHMAC.Encode(w);

            return ms.ToArray();
        }





        void GetSignedFieldsForSenderHMAC(BinaryWriter w)
        {
            GetSharedSignedFields(w);
            RequesterSignature.Encode(w);
            NhaSeq16.Encode(w);
        }
        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(Timestamp32S);
            RequesterPublicKey.Encode(w);
            ResponderPublicKey.Encode(w);
            PacketProcedures.EncodeByteArray65536(w, ToRequesterSessionDescriptionEncrypted);
        }

        public static InviteAck1Packet Decode_VerifySenderHMAC(byte[] udpPayloadData, ConnectionToNeighbor receivedFromNeighbor)
        {
            var r = new InviteAck1Packet();
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
            r.ToRequesterSessionDescriptionEncrypted = PacketProcedures.DecodeByteArray65536(reader);
            r.RequesterSignature = RegistrationSignature.Decode(reader);          
            r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            r.SenderHMAC = HMAC.Decode(reader);
            if (r.SenderHMAC.Equals(receivedFromNeighbor.GetSenderHMAC(r.GetSignedFieldsForSenderHMAC)) == false)
                throw new BadSignatureException();

            return r;
        }

    }
}
