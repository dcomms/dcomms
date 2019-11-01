using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// a part of REQ-NPACK -FAILURE-NPACK sequence
    /// is sent to requester side in response to INVITE or REGISTER, in case of an error
    /// 
    /// the FAILURE is retransmitted until NPACK with same ReqP2pSeq16
    /// </summary>
    class FailurePacket
    {
        public static byte Flag_EPtoA = 0x01; // set if packet is transmitted from EP to registering A, otherwise it is zero
        public byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        NeighborToken32 NeighborToken32; // is not sent from EP to A
        
        /// <summary>
        /// is same as REGISTER/INVITE ReqP2pSeq16
        /// </summary>
        public RequestP2pSequenceNumber16 ReqP2pSeq16;

        public ResponseOrFailureCode ResponseCode;

        HMAC NeighborHMAC; // is not sent from EP to A
                
        public byte[] DecodedUdpPayloadData;

        /// <summary>
        /// decodes the packet, verifies match to REQ
        /// </summary>
        /// <param name="reader">is positioned after first byte = packet type</param>    
        public static FailurePacket DecodeAndOptionallyVerify(byte[] failureUdpData, RequestP2pSequenceNumber16 reqP2pSeq16)
        {
            var reader = PacketProcedures.CreateBinaryReader(failureUdpData, 1);
            var failure = new FailurePacket();
            failure.DecodedUdpPayloadData = failureUdpData;
            failure.Flags = reader.ReadByte();
            if ((failure.Flags & Flag_EPtoA) == 0) failure.NeighborToken32 = NeighborToken32.Decode(reader);
            if ((failure.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            failure.ReqP2pSeq16 = RequestP2pSequenceNumber16.Decode(reader);
            failure.AssertMatchToReq(reqP2pSeq16);
            failure.ResponseCode = (ResponseOrFailureCode)reader.ReadByte();
                          
            if ((failure.Flags & Flag_EPtoA) == 0)
            {
                failure.NeighborHMAC = HMAC.Decode(reader);
            }

            return failure;
        }

        void AssertMatchToReq(RequestP2pSequenceNumber16 reqP2pSeq16)
        {
            if (reqP2pSeq16.Equals(this.ReqP2pSeq16) == false)
                throw new UnmatchedFieldsException();
        }        

        /// <param name="reqReceivedFromInP2pMode">is not null for packets between registered peers</param>
        public byte[] Encode_OpionallySignNeighborHMAC(ConnectionToNeighbor reqReceivedFromInP2pMode)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)PacketTypes.Failure);
            byte flags = 0;
            if (reqReceivedFromInP2pMode == null) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (reqReceivedFromInP2pMode != null)
            {
                NeighborToken32 = reqReceivedFromInP2pMode.RemoteNeighborToken32;
                NeighborToken32.Encode(writer);
            }

            ReqP2pSeq16.Encode(writer);
            writer.Write((byte)ResponseCode);

            if (reqReceivedFromInP2pMode != null)
            {
                this.NeighborHMAC = reqReceivedFromInP2pMode.GetNeighborHMAC(this.GetSignedFieldsForNeighborHMAC);
                this.NeighborHMAC.Encode(writer);
            }
         
            return ms.ToArray();

        }
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter w)
        {
            NeighborToken32.Encode(w);
            ReqP2pSeq16.Encode(w);
            w.Write((byte)ResponseCode);
        }

        /// <summary>
        /// creates a scanner that finds ACK1 that matches to REQ
        /// </summary>
        /// <param name="connectionToNeighborNullable">
        /// peer that responds to REQ with ACK1
        /// if not null - the scanner will verify ACK1.NeighborHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(Logger logger, RequestP2pSequenceNumber16 reqP2pSeq16, ConnectionToNeighbor connectionToNeighborNullable = null)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)PacketTypes.Failure);
            w.Write((byte)0);
            if (connectionToNeighborNullable != null)
                connectionToNeighborNullable.LocalNeighborToken32.Encode(w);

            reqP2pSeq16.Encode(w);

            var r = new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                IgnoredByteAtOffset1 = 1 // ignore flags
            };
            if (connectionToNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    if (connectionToNeighborNullable.IsDisposed)
                    {
                        logger.WriteToLog_needsAttention("ignoring FAILURE: connection is disposed");
                        return false;
                    }
                    var failure = DecodeAndOptionallyVerify(responseData, reqP2pSeq16);
                    if (failure.NeighborHMAC.Equals(connectionToNeighborNullable.GetNeighborHMAC(failure.GetSignedFieldsForNeighborHMAC)) == false)
                    {
                        logger.WriteToLog_attacks("ignoring FAILURE: received HMAC is invalid");
                        return false;
                    }
                    return true;
                };
            }
            return r;
        }

    }
}
