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
    class NeighborPeerAckPacket
    {
        public NeighborPeerAckSequenceNumber16 NpaSeq16;
        public const byte Flag_EPtoA = 0x01; // set if packet is transmitted from EP to A, is zero otherwise
        byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

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

        public NeighborPeerAckPacket()
        { }

        /// <param name="waitNhaFromNeighborNullable">is used to verify NPACK.SenderHMAC</param>
        public static LowLevelUdpResponseScanner GetScanner(NeighborPeerAckSequenceNumber16 npaSeq16, ConnectionToNeighbor waitNhaFromNeighborNullable = null, 
            Action<BinaryWriter> nhaRequestPacketFieldsForHmacNullable = null)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            EncodeHeader(w, npaSeq16);
            var r = new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
            if (waitNhaFromNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    var npack = new NeighborPeerAckPacket(responseData);
                    if (npack.SenderHMAC == null) return false;
                    if (!npack.SenderHMAC.Equals(waitNhaFromNeighborNullable.GetSenderHMAC(w2 => npack.GetFieldsForHMAC(w2, nhaRequestPacketFieldsForHmacNullable)))) return false;
                    return true;
                };
            }
            return r;
        }
        static void EncodeHeader(BinaryWriter w, NeighborPeerAckSequenceNumber16 npaSeq16)
        {
            w.Write((byte)DrpPacketType.NextHopAck);
            npaSeq16.Encode(w);
        }
        public byte[] Encode(bool epToA)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            EncodeHeader(writer, NpaSeq16);
            byte flags = 0;
            if (epToA) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (epToA == false)SenderToken32.Encode(writer);
            writer.Write((byte)StatusCode);
            if (epToA == false)SenderHMAC.Encode(writer);
            return ms.ToArray();
        }
        public NeighborPeerAckPacket(byte[] nextHopResponsePacketData)
        {
            var reader = PacketProcedures.CreateBinaryReader(nextHopResponsePacketData, 1);
            NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            if ((flags & Flag_EPtoA) == 0) SenderToken32 = P2pConnectionToken32.Decode(reader);
            StatusCode = (NextHopResponseCode)reader.ReadByte();
            if ((flags & Flag_EPtoA) == 0) SenderHMAC = HMAC.Decode(reader);
        } 

        internal void GetFieldsForHMAC(BinaryWriter w, Action<BinaryWriter> nhaRequestPacketFieldsForHMAC)
        {
            EncodeHeader(w, NpaSeq16);
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
        public DrpTimeoutException(string message = "Timeout while waiting for response")
            : base(message)
        {

        }

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
    /// gets copied from request/response packets  to "neighborPeerACK" packet
    /// </summary>
    public class NeighborPeerAckSequenceNumber16
    {
        public ushort Seq16;
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Seq16);
        }
        public static NeighborPeerAckSequenceNumber16 Decode(BinaryReader reader)
        {
            var r = new NeighborPeerAckSequenceNumber16();
            r.Seq16 = reader.ReadUInt16();
            return r;
        }
        public override bool Equals(object obj)
        {
            return ((NeighborPeerAckSequenceNumber16)obj).Seq16 == this.Seq16;
        }
        public override string ToString() => $"npaSeq{Seq16}";
    }

}
