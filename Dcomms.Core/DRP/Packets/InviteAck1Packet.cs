using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    class InviteAck1Packet
    {
        // byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;
        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public P2pConnectionToken32 SenderToken32;

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
            w.Write((byte)DrpPacketType.InviteAck1);
            byte flags = 0;
            w.Write(flags);

            NhaSeq16 = transmitToNeighbor.GetNewNhaSeq16_P2P();
            SenderToken32 = transmitToNeighbor.RemotePeerToken32;
            SenderToken32.Encode(w);

            GetSignedFieldsForSenderHMAC(w);

            SenderHMAC = transmitToNeighbor.GetSenderHMAC(GetSignedFieldsForSenderHMAC);
            SenderHMAC.Encode(w);

            return ms.ToArray();
        }
                     

        internal void GetSignedFieldsForSenderHMAC(BinaryWriter w)
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

        internal byte[] DecodedUdpPayloadData;
        public static InviteAck1Packet Decode(byte[] udpPayloadData)
        {
            var r = new InviteAck1Packet();
            r.DecodedUdpPayloadData = udpPayloadData;
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.SenderToken32 = P2pConnectionToken32.Decode(reader);

            r.Timestamp32S = reader.ReadUInt32();
            r.RequesterPublicKey = RegistrationPublicKey.Decode(reader);
            r.ResponderPublicKey = RegistrationPublicKey.Decode(reader);
            r.ToRequesterSessionDescriptionEncrypted = PacketProcedures.DecodeByteArray65536(reader);
            r.RequesterSignature = RegistrationSignature.Decode(reader);          
            r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            r.SenderHMAC = HMAC.Decode(reader);
            return r;
        }



        /// <summary>
        /// creates a scanner that finds ACK1 that matches to SYN
        /// the scanner will verify ACK1.SenderHMAC
        /// </summary>
        /// <param name="connectionToNeighbor">
        /// requester peer that sends ACK1
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(InviteSynPacket syn, ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteAck1);
            w.Write((byte)0); // flags

            connectionToNeighbor.LocalRxToken32.Encode(w);

            w.Write(syn.Timestamp32S);
            syn.RequesterPublicKey.Encode(w);
            syn.ResponderPublicKey.Encode(w);

            var r = new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                IgnoredByteAtOffset1 = 1 // ignore flags
            };

            r.OptionalFilter = (responseData) =>
            {
                var ack1 = Decode(responseData);
                if (ack1.SenderHMAC.Equals(connectionToNeighbor.GetSenderHMAC(ack1.GetSignedFieldsForSenderHMAC)) == false) return false;
                return true;
            };

            return r;
        }

    }
}
