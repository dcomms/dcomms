using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    class InviteAck2Packet
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

        public RegistrationSignature ResponderSignature;

        public NextHopAckSequenceNumber16 NhaSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC SenderHMAC;

        public byte[] Encode(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteAck2);
            byte flags = 0;
            w.Write(flags);

            SenderToken32 = transmitToNeighbor.RemotePeerToken32;
            SenderToken32.Encode(w);

            GetSignedFieldsForSenderHMAC(w);

            SenderHMAC = transmitToNeighbor.GetSenderHMAC(GetSignedFieldsForSenderHMAC);
            SenderHMAC.Encode(w);

            return ms.ToArray();
        }

        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(Timestamp32S);
            RequesterPublicKey.Encode(w);
            ResponderPublicKey.Encode(w);
        }
        void GetSignedFieldsForSenderHMAC(BinaryWriter w)
        {
            GetSharedSignedFields(w);
            ResponderSignature.Encode(w);
            NhaSeq16.Encode(w);
        }

        public static InviteAck2Packet Decode(byte[] udpPayloadData)
        {
            var r = new InviteAck2Packet();
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.SenderToken32 = P2pConnectionToken32.Decode(reader);

            r.Timestamp32S = reader.ReadUInt32();
            r.RequesterPublicKey = RegistrationPublicKey.Decode(reader);
            r.ResponderPublicKey = RegistrationPublicKey.Decode(reader);
            r.ResponderSignature = RegistrationSignature.Decode(reader);
            r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            r.SenderHMAC = HMAC.Decode(reader);

            return r;
        }



        /// <summary>
        /// creates a scanner that finds ACK2 that matches to SYN
        /// the scanner will verify ACK2.SenderHMAC
        /// </summary>
        /// <param name="connectionToNeighbor">
        /// peer that responds with ACK2
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(InviteSynPacket syn, ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteSynAck);
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
                var ack2 = Decode(responseData);
                if (ack2.SenderHMAC.Equals(connectionToNeighbor.GetSenderHMAC(ack2.GetSignedFieldsForSenderHMAC)) == false) return false;
                return true;
            };

            return r;
        }

    }
}
