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
        public NeighborToken32 NeighborToken32;

        public uint ReqTimestamp32S;
        public RegistrationId RequesterRegistrationId; // A public key 
        public RegistrationId ResponderRegistrationId; // B public key

        public byte[] ToRequesterSessionDescriptionEncrypted;
        public RegistrationSignature RequesterRegistrationSignature;
        
        public RequestP2pSequenceNumber16 ReqP2pSeq16;

        /// <summary>
        /// authorizes peer that sends the packet
        /// </summary>
        public HMAC NeighborHMAC;

        public byte[] Encode_SetP2pFields(ConnectionToNeighbor transmitToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)PacketTypes.InviteAck2);
            byte flags = 0;
            w.Write(flags);

            ReqP2pSeq16 = transmitToNeighbor.GetNewRequestP2pSeq16_P2P();
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
            ReqP2pSeq16.Encode(w);
        }
        internal void GetSharedSignedFields(BinaryWriter w)
        {
            w.Write(ReqTimestamp32S);
            RequesterRegistrationId.Encode(w);
            ResponderRegistrationId.Encode(w);
            PacketProcedures.EncodeByteArray65536(w, ToRequesterSessionDescriptionEncrypted);
        }

        internal byte[] DecodedUdpPayloadData;
        public static InviteAck2Packet Decode(byte[] udpData)
        {
            var r = new InviteAck2Packet();
            r.DecodedUdpPayloadData = udpData;
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.NeighborToken32 = NeighborToken32.Decode(reader);

            r.ReqTimestamp32S = reader.ReadUInt32();
            r.RequesterRegistrationId = RegistrationId.Decode(reader);
            r.ResponderRegistrationId = RegistrationId.Decode(reader);
            r.ToRequesterSessionDescriptionEncrypted = PacketProcedures.DecodeByteArray65536(reader);
            r.RequesterRegistrationSignature = RegistrationSignature.Decode(reader);          
            r.ReqP2pSeq16 = RequestP2pSequenceNumber16.Decode(reader);

            r.NeighborHMAC = HMAC.Decode(reader);
            return r;
        }



        /// <summary>
        /// creates a scanner that finds ACK2 that matches to REQ
        /// the scanner will verify ACK2.NeighborHMAC
        /// </summary>
        /// <param name="connectionToNeighbor">
        /// peer that sends ACK2
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(Logger logger, InviteRequestPacket req, ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)PacketTypes.InviteAck2);
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
                if (connectionToNeighbor.IsDisposed)
                {
                    logger.WriteToLog_needsAttention("ignoring ACK2: connection is disposed");
                    return false;
                }
                var ack2 = Decode(responseData);
                if (ack2.NeighborHMAC.Equals(connectionToNeighbor.GetNeighborHMAC(ack2.GetSignedFieldsForNeighborHMAC)) == false)
                {
                    logger.WriteToLog_attacks("ignoring ACK2: received NeighborHMAC is invalid");
                    return false;
                }
                return true;
            };

            return r;
        }

    }
}
