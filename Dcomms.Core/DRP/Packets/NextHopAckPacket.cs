using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{


    /// <summary>
    /// is sent from next hop to previous hop, when the next hop receives some packet from neighbor, or from registering peer (RP->A).
    /// stops UDP retransmission of a request packet
    /// </summary>
    class NextHopAckPacket
    {
        public NextHopAckSequenceNumber16 NhaSeq16;
        public const byte Flag_RPtoA = 0x01; // set if packet is transmitted from RP to A, is zero otherwise
        byte Flags;
        public P2pConnectionToken32 SenderToken32; // is not transmitted in RP->A packet
        public NextHopResponseCode StatusCode;
        /// <summary>
        /// signature of sender neighbor peer
        /// is NULL for RP->A packet
        /// uses common secret of neighbors within P2P connection
        /// </summary>
        public HMAC SenderHMAC;

        public NextHopAckPacket()
        { }
        public static void EncodeHeader(BinaryWriter writer, NextHopAckSequenceNumber16 nhaSeq16)
        {
            writer.Write((byte)DrpPacketType.NextHopAckPacket);
            nhaSeq16.Encode(writer);
        }
        public byte[] Encode(bool rpToA)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            EncodeHeader(writer, NhaSeq16);
            byte flags = 0;
            if (rpToA) flags |= Flag_RPtoA;
            writer.Write(flags);
            if (rpToA == false) SenderToken32.Encode(writer);
            writer.Write((byte)StatusCode);
            if (rpToA == false) SenderHMAC.Encode(writer);
            return ms.ToArray();
        }
        public NextHopAckPacket(byte[] nextHopResponsePacketData)
        {
            var reader = PacketProcedures.CreateBinaryReader(nextHopResponsePacketData, 1);
            NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
            var flags = reader.ReadByte();
            if ((flags & Flag_RPtoA) == 0) SenderToken32 = P2pConnectionToken32.Decode(reader);
            StatusCode = (NextHopResponseCode)reader.ReadByte();
            if ((flags & Flag_RPtoA) == 0) SenderHMAC = HMAC.Decode(reader);
        }
    }
    enum NextHopResponseCode
    {
        accepted, // is sent to previous hop immediately when packet is proxied, to stop retransmission timer
        rejected_overloaded,
        rejected_rateExceeded, // anti-ddos
    }



    class NextHopTimeoutException : ApplicationException
    {

    }
    class NextHopRejectedException : ApplicationException
    {
        public NextHopRejectedException(NextHopResponseCode responseCode)
            : base($"Next hop rejected request with status = {responseCode}")
        {

        }
    }


    /// <summary>
    /// gets copied from request/response packets  to "nextHopACK" packet
    /// </summary>
    public class NextHopAckSequenceNumber16
    {
        public ushort Seq16;
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Seq16);
        }
        public static NextHopAckSequenceNumber16 Decode(BinaryReader reader)
        {
            var r = new NextHopAckSequenceNumber16();
            r.Seq16 = reader.ReadUInt16();
            return r;
        }
        public override bool Equals(object obj)
        {
            return ((NextHopAckSequenceNumber16)obj).Seq16 == this.Seq16;
        }
    }

}
