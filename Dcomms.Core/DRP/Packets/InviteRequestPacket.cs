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
        // byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public NeighborToken32 NeighborToken32;

        public uint ReqTimestamp32S;
        public RegistrationId RequesterRegistrationId; // A public key 
        public RegistrationId ResponderRegistrationId; // B public key
        public EcdhPublicKey RequesterEcdhePublicKey; // for ephemeral private EC key generated at requester (A) specifically for the new DirectChannel connection
        public RegistrationSignature RequesterRegistrationSignature;

        public byte NumberOfHopsRemaining; // is decremented by peers
        public const byte MaxNumberOfHopsRemaining = 30;

        public RequestP2pSequenceNumber16 ReqP2pSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC NeighborHMAC;

        public byte[] Encode_SetP2pFields(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.InviteReq);
            byte flags = 0;
            w.Write(flags);

            ReqP2pSeq16 = transmitToNeighbor.GetNewNpaSeq16_P2P();
            NeighborToken32 = transmitToNeighbor.RemoteNeighborToken32;
            NeighborToken32.Encode(w);

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
            ReqP2pSeq16.Encode(w);
        }
        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(ReqTimestamp32S);
            RequesterRegistrationId.Encode(w);
            ResponderRegistrationId.Encode(w);
            RequesterEcdhePublicKey.Encode(w);
        }

        internal byte[] DecodedUdpPayloadData;
        public static InviteRequestPacket Decode_VerifyNeighborHMAC(byte[] udpData, ConnectionToNeighbor receivedFromNeighbor)
        {
            var r = new InviteRequestPacket();
            r.DecodedUdpPayloadData = udpData;
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.NeighborToken32 = NeighborToken32.Decode(reader);
            if (receivedFromNeighbor.LocalNeighborToken32.Equals(r.NeighborToken32) == false)
                throw new UnmatchedFieldsException();

            r.ReqTimestamp32S = reader.ReadUInt32();
            r.RequesterRegistrationId = RegistrationId.Decode(reader);
            r.ResponderRegistrationId = RegistrationId.Decode(reader);
            r.RequesterEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.RequesterRegistrationSignature = RegistrationSignature.Decode(reader);
            r.NumberOfHopsRemaining = reader.ReadByte();
            r.ReqP2pSeq16 = RequestP2pSequenceNumber16.Decode(reader);

            r.NeighborHMAC = HMAC.Decode(reader);
            if (r.NeighborHMAC.Equals(receivedFromNeighbor.GetNeighborHMAC(r.GetSignedFieldsForNeighborHMAC)) == false)
                throw new BadSignatureException();

            return r;
        }
        
        public static ushort DecodeNeighborToken16(byte[] udpData)
        { // first 2 bytes ares packet type and flags. then 4 bytes are NeighborToken32
            return (ushort)(udpData[2] | (udpData[3] << 8));
        }
        public void GetUniqueRequestIdFields(BinaryWriter writer)
        {
            RequesterRegistrationId.Encode(writer);
            ResponderRegistrationId.Encode(writer);
            writer.Write(ReqTimestamp32S);
        }

        public override bool Equals(object obj)
        {
            var obj2 = obj as InviteRequestPacket;
            if (obj2 == null) return false;
            return obj2.ReqTimestamp32S == this.ReqTimestamp32S && obj2.RequesterRegistrationId.Equals(this.RequesterRegistrationId);
        }
        public override int GetHashCode()
        {
            return ReqTimestamp32S.GetHashCode() ^ RequesterRegistrationId.GetHashCode();
        }
        public override string ToString() => $"invReq[from{RequesterRegistrationId}-{ReqTimestamp32S}-{RequesterEcdhePublicKey}to{ResponderRegistrationId}]";
    }
}
