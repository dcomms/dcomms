using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{


    /// <summary>
    /// is sent from next hop to previous hop, when the next hop receives some packet from neighbor, or from registering peer (EP->A).
    /// is also sent from A to EP in response to erroneous ACK1
    /// stops UDP retransmission of a request packet
    /// 
    /// </summary>
    class NeighborPeerAckPacket
    {
        public RequestP2pSequenceNumber16 ReqP2pSeq16;
        public const byte Flag_EPtoA = 0x01; // set if packet is transmitted from EP to A, is zero otherwise
        byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32 in case when this packet goes ofver established P2P connection (flag A-EP is zero)
        /// </summary>
        public NeighborToken32 NeighborToken32; 
        public ResponseOrFailureCode ResponseCode;
        /// <summary>
        /// signature of sender neighbor peer
        /// is NULL for EP->A packet
        /// uses common secret of neighbors within P2P connection
        /// </summary>
        public HMAC NeighborHMAC;

        public NeighborPeerAckPacket()
        { }

        /// <param name="waitNpaFromNeighborNullable">is used to verify NPACK.NeighborHMAC</param>
        public static LowLevelUdpResponseScanner GetScanner(RequestP2pSequenceNumber16 reqP2pSeq16, ConnectionToNeighbor waitNpaFromNeighborNullable = null, 
            Action<BinaryWriter> npaRequestFieldsForNeighborHmacNullable = null)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            EncodeHeader(w, reqP2pSeq16);
            var r = new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
            if (waitNpaFromNeighborNullable != null)
            {
                if (npaRequestFieldsForNeighborHmacNullable == null) throw new ArgumentNullException();
                r.OptionalFilter = (responseData) =>
                {
                    if (waitNpaFromNeighborNullable.IsDisposed) return false;
                    var npack = new NeighborPeerAckPacket(responseData);
                    if (npack.NeighborHMAC == null) return false;
                    if (!npack.NeighborHMAC.Equals(waitNpaFromNeighborNullable.GetNeighborHMAC(w2 => npack.GetSignedFieldsForNeighborHMAC(w2, npaRequestFieldsForNeighborHmacNullable)))) return false;
                    return true;
                };
            }
            return r;
        }
        static void EncodeHeader(BinaryWriter w, RequestP2pSequenceNumber16 reqP2pSeq16)
        {
            w.Write((byte)PacketTypes.NeighborPeerAck);
            reqP2pSeq16.Encode(w);
        }
        public byte[] Encode(bool epToA)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            EncodeHeader(writer, ReqP2pSeq16);
            byte flags = 0;
            if (epToA) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (epToA == false)NeighborToken32.Encode(writer);
            writer.Write((byte)ResponseCode);
            if (epToA == false)NeighborHMAC.Encode(writer);
            return ms.ToArray();
        }
        public NeighborPeerAckPacket(byte[] nextHopResponsePacketData)
        {
            var reader = BinaryProcedures.CreateBinaryReader(nextHopResponsePacketData, 1);
            ReqP2pSeq16 = RequestP2pSequenceNumber16.Decode(reader);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            if ((flags & Flag_EPtoA) == 0) NeighborToken32 = NeighborToken32.Decode(reader);
            ResponseCode = (ResponseOrFailureCode)reader.ReadByte();
            if ((flags & Flag_EPtoA) == 0) NeighborHMAC = HMAC.Decode(reader);
        } 

        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter w, Action<BinaryWriter> nhaRequestPacketFieldsForHMAC)
        {
            if (nhaRequestPacketFieldsForHMAC == null) throw new ArgumentNullException();
            EncodeHeader(w, ReqP2pSeq16);
            NeighborToken32.Encode(w); // it is not null, if we verify HMAC
            w.Write((byte)ResponseCode);
            nhaRequestPacketFieldsForHMAC(w);
        }
    }
  



}
