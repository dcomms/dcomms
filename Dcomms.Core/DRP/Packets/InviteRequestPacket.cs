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
        public NeighborToken32 NeighborToken32;
        // byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public uint ReqTimestamp32S;
        public RegistrationId RequesterRegistrationId; // A public key 
        public RegistrationId ResponderPublicKey; // B public key
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new DirectChannel connection
        public RegistrationSignature RequesterRegistrationSignature;

        public byte NumberOfHopsRemaining; // max 10 // is decremented by peers

        public NeighborPeerAckSequenceNumber16 NpaSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC NeighborHMAC;

        public byte[] Encode_SetP2pFields(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.InviteReq);

            NpaSeq16 = transmitToNeighbor.GetNewNpaSeq16_P2P();
            NeighborToken32 = transmitToNeighbor.RemoteNeighborToken32;
            NeighborToken32.Encode(w);

            byte flags = 0;
            w.Write(flags);
            GetSignedFieldsForNeighborHMAC(w);

            NeighborHMAC = transmitToNeighbor.GetNeighborHMAC(GetSignedFieldsForNeighborHMAC);
            NeighborHMAC.Encode(w);

            return ms.ToArray();
        }
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter w)
        {
            GetSharedSignedFields(w);
            RequesterRegistrationSignature.Encode(w);
            w.Write(NumberOfHopsRemaining);
            NpaSeq16.Encode(w);
        }
        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(ReqTimestamp32S);
            RequesterRegistrationId.Encode(w);
            ResponderPublicKey.Encode(w);
            RequesterEcdhePublicKey.Encode(w);
        }

        internal byte[] DecodedUdpPayloadData;
        public static InviteRequestPacket Decode_VerifyNeighborHMAC(byte[] udpPayloadData, ConnectionToNeighbor receivedFromNeighbor)
        {
            var r = new InviteRequestPacket();
            r.DecodedUdpPayloadData = udpPayloadData;
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            r.NeighborToken32 = NeighborToken32.Decode(reader);
            if (receivedFromNeighbor.LocalNeighborToken32.Equals(r.NeighborToken32) == false)
                throw new UnmatchedFieldsException();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.ReqTimestamp32S = reader.ReadUInt32();
            r.RequesterRegistrationId = RegistrationId.Decode(reader);
            r.ResponderPublicKey = RegistrationId.Decode(reader);
            r.RequesterEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.RequesterRegistrationSignature = RegistrationSignature.Decode(reader);
            r.NumberOfHopsRemaining = reader.ReadByte();
            r.NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);

            r.NeighborHMAC = HMAC.Decode(reader);
            if (r.NeighborHMAC.Equals(receivedFromNeighbor.GetNeighborHMAC(r.GetSignedFieldsForNeighborHMAC)) == false)
                throw new BadSignatureException();

            return r;
        }
        public void GetUniqueRequestIdFields(BinaryWriter writer)
        {
            RequesterRegistrationId.Encode(writer);
            ResponderPublicKey.Encode(writer);
            writer.Write(ReqTimestamp32S);
        }
    }
}
