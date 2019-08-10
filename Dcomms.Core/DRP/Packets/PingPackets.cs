using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    public class PingRequestPacket
    {
        public P2pConnectionToken32 SenderToken32;
        public byte ReservedFlags;
        public uint TimestampMs32; // msec // is used as salt also
        public float? MaxRxInviteRateRps;   // zero means NULL // signal from sender "how much I can receive via this p2p connection"
        public float? MaxRxRegisterRateRps; // zero means NULL // signal from sender "how much I can receive via this p2p connection"
        public HMAC SenderHMAC; // signs fields, to authenticate the request

        static ushort RpsToUint16(float? rps) // resulution=0.01 RPS    max value=0.65K RPS
        {
            return (ushort)Math.Round(Math.Min(65535, (rps ?? 0) * 100));
        }
        public void GetSignedFields(BinaryWriter writer)
        {
            SenderToken32.Encode(writer);
            writer.Write(ReservedFlags);
            writer.Write(TimestampMs32);
            writer.Write(RpsToUint16(MaxRxInviteRateRps));
            writer.Write(RpsToUint16(MaxRxRegisterRateRps));
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpPacketType.PingRequestPacket);
            GetSignedFields(writer);
            return ms.ToArray();
        }
    }
    public class PingResponsePacket
    {
        byte Flags;
        const byte Flags_P2pConnectionSetupSignatureExists = 0x01;
        public P2pConnectionToken32 SenderToken32;
        /// <summary>
        /// comes from responder neighbor when connection is set up; in other cases it is NULL
        /// signs fields: 
        /// { 
        ///    from syn packet:  RequesterPublicKey_RequestID,Timestamp32S,
        ///    from synAck packet: NeighborPublicKey,
        ///    P2pConnectionSetupSignature_MagicNumber
        /// } by neighbor's reg. private key
        /// is verified by RP, X to update rating of responder neighbor
        /// </summary>
        public RegistrationSignature P2pConnectionSetupSignature;

        public uint PingRequestTimestampMs32;  // must match to request
        public HMAC SenderHMAC; // signs { Flags,SenderToken32,(optional)P2pConnectionSetupSignature,PingRequestTimestampMs32 }

        /// <param name="reader">is positioned after first byte = packet type</param>
        public static PingResponsePacket DecodeAndVerify(ICryptoLibrary cryptoLibrary, BinaryReader reader, 
            PingRequestPacket pingRequestPacket, 
            ConnectedDrpPeer connectedPeerWhoSentTheResponse, 
            bool requireSignature,
            RegisterSynPacket registerSynPacket, 
            RegisterSynAckPacket registerSynAckPacket
            )
        {
            var r = new PingResponsePacket();
            r.Flags = reader.ReadByte();            
            r.SenderToken32 = P2pConnectionToken32.Decode(reader);
 
            // verify signature of N
            if ((r.Flags & Flags_P2pConnectionSetupSignatureExists) != 0)
                r.P2pConnectionSetupSignature = RegistrationSignature.DecodeAndVerify(reader, cryptoLibrary, 
                    w=> GetSignedFieldsForP2pConnectionSetupSignature(w, registerSynPacket, registerSynAckPacket), 
                    connectedPeerWhoSentTheResponse.RemotePeerPublicKey);
            else
            {
                if (requireSignature) throw new UnmatchedResponseFieldsException();
            }

            r.PingRequestTimestampMs32 = reader.ReadUInt32();
            r.SenderHMAC = HMAC.Decode(reader);

            // verify PingRequestTimestampMs32
            if (r.PingRequestTimestampMs32 != pingRequestPacket.TimestampMs32)
                throw new UnmatchedResponseFieldsException();

            // verify SenderToken32
            if (!r.SenderToken32.Equals(connectedPeerWhoSentTheResponse.LocalRxToken32))
                throw new UnmatchedResponseFieldsException();

            // verify SenderHMAC
            if (r.SenderHMAC.Equals(
                connectedPeerWhoSentTheResponse.TxParameters.GetSharedHmac(cryptoLibrary, r.GetSignedFieldsForSenderHMAC)
                ) == false)
                throw new BadSignatureException();

          
            return r;
        }
               

        public void GetSignedFieldsForSenderHMAC(BinaryWriter writer)
        {
            writer.Write(Flags);
            SenderToken32.Encode(writer);
            if (P2pConnectionSetupSignature != null)
                P2pConnectionSetupSignature.Encode(writer);
            writer.Write(PingRequestTimestampMs32);
        }
        const ushort P2pConnectionSetupSignature_MagicNumber = 0x7827;
        static void GetSignedFieldsForP2pConnectionSetupSignature(BinaryWriter writer, RegisterSynPacket registerSynPacket, RegisterSynAckPacket registerSynAckPacket)
        {
            registerSynPacket.RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(registerSynPacket.Timestamp32S);
            registerSynAckPacket.NeighborPublicKey.Encode(writer);
            writer.Write(P2pConnectionSetupSignature_MagicNumber);
        }
    }
}
