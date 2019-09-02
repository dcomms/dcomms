using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{


    /// <summary>
    /// is sent from next hop to previous hop, when the next hop receives some packet from neighbor, or from registering peer (EP->A).
    /// stops UDP retransmission of a request packet
    /// </summary>
    class NextHopAckPacket
    {
        public NextHopAckSequenceNumber16 NhaSeq16;
        public const byte Flag_EPtoA = 0x01; // set if packet is transmitted from EP to A, is zero otherwise
        byte Flags;

        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemotePeerToken32 in case when this packet goes ofver established P2P connection (flag A-EP is zero)
        /// </summary>
        public P2pConnectionToken32 SenderToken32; 
        public NextHopResponseCode StatusCode;
        /// <summary>
        /// signature of sender neighbor peer
        /// is NULL for EP->A packet
        /// uses common secret of neighbors within P2P connection
        /// </summary>
        public HMAC SenderHMAC;

        public NextHopAckPacket()
        { }

        /// <param name="waitNhaFromNeighborNullable">is used to verify NHACK.SenderHMAC</param>
        public static LowLevelUdpResponseScanner GetScanner(NextHopAckSequenceNumber16 nhaSeq16, ConnectionToNeighbor waitNhaFromNeighborNullable = null, 
            Action<BinaryWriter> nhaRequestPacketFieldsForHmacNullable = null)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            EncodeHeader(w, nhaSeq16);
            var r = new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
            if (waitNhaFromNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    var nhack = new NextHopAckPacket(responseData);
                    if (nhack.SenderHMAC == null) return false;
                    if (!nhack.SenderHMAC.Equals(waitNhaFromNeighborNullable.GetSharedHMAC(w2 => nhack.GetFieldsForHMAC(w2, nhaRequestPacketFieldsForHmacNullable)))) return false;
                    return true;
                };
            }
            return r;
        }
        static void EncodeHeader(BinaryWriter w, NextHopAckSequenceNumber16 nhaSeq16)
        {
            w.Write((byte)DrpPacketType.NextHopAck);
            nhaSeq16.Encode(w);
        }
        public byte[] Encode(bool rpToA)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            EncodeHeader(writer, NhaSeq16);
            byte flags = 0;
            if (rpToA) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (rpToA == false)SenderToken32.Encode(writer);
            writer.Write((byte)StatusCode);
            if (rpToA == false)SenderHMAC.Encode(writer);
            return ms.ToArray();
        }
        public NextHopAckPacket(byte[] nextHopResponsePacketData)
        {
            var reader = PacketProcedures.CreateBinaryReader(nextHopResponsePacketData, 1);
            NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
            var flags = reader.ReadByte();
            if ((flags & Flag_EPtoA) == 0) SenderToken32 = P2pConnectionToken32.Decode(reader);
            StatusCode = (NextHopResponseCode)reader.ReadByte();
            if ((flags & Flag_EPtoA) == 0) SenderHMAC = HMAC.Decode(reader);
        } 

        internal void GetFieldsForHMAC(BinaryWriter w, Action<BinaryWriter> nhaRequestPacketFieldsForHMAC)
        {
            EncodeHeader(w, NhaSeq16);
            SenderToken32.Encode(w); // it is not null, if we verify HMAC
            w.Write((byte)StatusCode);
            nhaRequestPacketFieldsForHMAC(w);
        }
    }
    enum NextHopResponseCode
    {
        accepted = 0, // is sent to previous hop immediately when packet is proxied, to stop retransmission timer
        rejected_overloaded = 1,
        rejected_rateExceeded = 2, // anti-ddos
        rejected_numberOfHopsRemainingReachedZero = 3
    }



    class DrpTimeoutException : ApplicationException // next hop or EP, or whatever responder timed out
    {

    }
    class NextHopRejectedException : ApplicationException
    {
        public NextHopRejectedException(NextHopResponseCode responseCode)
            : base($"Next hop rejected request with status = {responseCode}")
        {

        }
    }
    class Pow1RejectedException : ApplicationException
    {
        public Pow1RejectedException(RegisterPow1ResponseStatusCode responseCode)
            : base($"EP rejected PoW1 request with status = {responseCode}")
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
        public override string ToString() => $"hhaSeq{Seq16}";
    }

}
