using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    class InviteConfirmationPacket
    {
        // byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public NeighborToken32 NeighborToken32;

        public uint ReqTimestamp32S;
        public RegistrationId RequesterRegistrationId; // A public key 
        public RegistrationId ResponderRegistrationId; // B public key

        public RegistrationSignature ResponderRegistrationSignature;

        public NeighborPeerAckSequenceNumber16 NpaSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC NeighborHMAC;

        public byte[] Encode_SetP2pFields(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.InviteCfm);
            byte flags = 0;
            w.Write(flags);

            NeighborToken32 = transmitToNeighbor.RemoteNeighborToken32;
            NeighborToken32.Encode(w);

            NpaSeq16 = transmitToNeighbor.GetNewNpaSeq16_P2P();

            GetSignedFieldsForNeighborHMAC(w);

            NeighborHMAC = transmitToNeighbor.GetNeighborHMAC(GetSignedFieldsForNeighborHMAC);
            NeighborHMAC.Encode(w);

            return ms.ToArray();
        }

        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(ReqTimestamp32S);
            RequesterRegistrationId.Encode(w);
            ResponderRegistrationId.Encode(w);
        }
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter w)
        {
            GetSharedSignedFields(w);
            ResponderRegistrationSignature.Encode(w);
            NpaSeq16.Encode(w);
        }

        internal byte[] DecodedUdpPayloadData;
        public static InviteConfirmationPacket Decode(byte[] udpData)
        {
            var r = new InviteConfirmationPacket();
            r.DecodedUdpPayloadData = udpData;
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.NeighborToken32 = NeighborToken32.Decode(reader);

            r.ReqTimestamp32S = reader.ReadUInt32();
            r.RequesterRegistrationId = RegistrationId.Decode(reader);
            r.ResponderRegistrationId = RegistrationId.Decode(reader);
            r.ResponderRegistrationSignature = RegistrationSignature.Decode(reader);
            r.NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);

            r.NeighborHMAC = HMAC.Decode(reader);

            return r;
        }



        /// <summary>
        /// creates a scanner that finds CFM that matches to REQ
        /// the scanner will verify CFM.NeighborHMAC
        /// </summary>
        /// <param name="connectionToNeighbor">
        /// peer that responds with CFM
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(InviteRequestPacket req, ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.InviteCfm);
            w.Write((byte)0); // flags

            connectionToNeighbor.LocalNeighborToken32.Encode(w);

            w.Write(req.ReqTimestamp32S);
            req.RequesterRegistrationId.Encode(w);
            req.ResponderRegistrationId.Encode(w);

            var r = new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                IgnoredByteAtOffset1 = 1 // ignore flags
            };

            r.OptionalFilter = (responseData) =>
            {
                var cfm = Decode(responseData);
                if (cfm.NeighborHMAC.Equals(connectionToNeighbor.GetNeighborHMAC(cfm.GetSignedFieldsForNeighborHMAC)) == false) return false;
                return true;
            };

            return r;
        }

    }
}
