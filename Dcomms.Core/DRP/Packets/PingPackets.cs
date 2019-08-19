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
        public const byte Flags_P2pConnectionSetupSignatureRequested = 0x01;
        public byte Flags;
        public uint PingRequestId32; // is used to avoid mismatch between delyed responses and requests // is used as salt also
        public float? MaxRxInviteRateRps;   // zero means NULL // signal from sender "how much I can receive via this p2p connection"
        public float? MaxRxRegisterRateRps; // zero means NULL // signal from sender "how much I can receive via this p2p connection"
        public HMAC SenderHMAC; // signs fields, to authenticate the request

        static ushort RpsToUint16(float? rps) // resulution=0.01 RPS    max value=0.65K RPS
        {
            return (ushort)Math.Round(Math.Min(65535, (rps ?? 0) * 100));
        }
        static float? RpsFromUint16(ushort v)
        {
            return v != 0 ? (float?)((float)v * 0.01) : null;
        }
        public void GetSignedFieldsForSenderHMAC(BinaryWriter writer)
        {
            SenderToken32.Encode(writer);
            writer.Write(Flags);
            writer.Write(PingRequestId32);
            writer.Write(RpsToUint16(MaxRxInviteRateRps));
            writer.Write(RpsToUint16(MaxRxRegisterRateRps));
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpPacketType.PingRequestPacket);
            GetSignedFieldsForSenderHMAC(writer);
            return ms.ToArray();
        }

        public static ushort DecodeToken16FromUdpPayloadData(byte[] udpPayloadData)
        { // first byte is packet type. then 4 bytes are P2pConnectionToken32
            return (ushort)(udpPayloadData[1] | (udpPayloadData[2] << 8));
        }

        public static PingRequestPacket DecodeAndVerify(byte[] udpPayloadData, ConnectedDrpPeer connectedPeerWhoSentTheRequest, ICryptoLibrary cryptoLibrary)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            var r = new PingRequestPacket();
            r.SenderToken32 = P2pConnectionToken32.Decode(reader);
            r.Flags = reader.ReadByte();              
            r.PingRequestId32 = reader.ReadUInt32();
            r.MaxRxInviteRateRps = RpsFromUint16(reader.ReadUInt16());
            r.MaxRxRegisterRateRps = RpsFromUint16(reader.ReadUInt16());
            r.SenderHMAC = HMAC.Decode(reader);
            
            // verify SenderToken32
            if (!r.SenderToken32.Equals(connectedPeerWhoSentTheRequest.LocalRxToken32))
                throw new BadSignatureException();

            // verify SenderHMAC
            if (r.SenderHMAC.Equals(
                connectedPeerWhoSentTheRequest.TxParameters.GetSharedHmac(cryptoLibrary, r.GetSignedFieldsForSenderHMAC)
                ) == false)
                throw new BadSignatureException();

            return r;

        }
    }
    public class PingResponsePacket
    {
        /// <summary>
        /// authenticates sender peer at receiver side
        /// </summary>
        public P2pConnectionToken32 SenderToken32;
        public uint PingRequestId32;  // must match to request
        byte Flags;
        const byte Flags_P2pConnectionSetupSignatureExists = 0x01;
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

        public HMAC SenderHMAC; // signs { Flags,SenderToken32,(optional)P2pConnectionSetupSignature,PingRequestTimestampMs32 }

        /// <param name="reader">is positioned after first byte = packet type</param>
        public static PingResponsePacket DecodeAndVerify(ICryptoLibrary cryptoLibrary,
            byte[] udpPayloadData, 
            PingRequestPacket optionalPingRequestPacketToCheckRequestId32, 
            ConnectedDrpPeer connectedPeerWhoSentTheResponse, 
            bool requireSignature,
            RegisterSynPacket registerSynPacket, 
            RegisterSynAckPacket registerSynAckPacket
            )
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);
            var r = new PingResponsePacket();
            r.SenderToken32 = P2pConnectionToken32.Decode(reader);
            r.PingRequestId32 = reader.ReadUInt32();
            r.Flags = reader.ReadByte();            
 
            // verify signature of N
            if ((r.Flags & Flags_P2pConnectionSetupSignatureExists) != 0)
                r.P2pConnectionSetupSignature = RegistrationSignature.DecodeAndVerify(reader, cryptoLibrary, 
                    w=> GetSignedFieldsForP2pConnectionSetupSignature(w, registerSynPacket, registerSynAckPacket), 
                    connectedPeerWhoSentTheResponse.RemotePeerPublicKey);
            else
            {
                if (requireSignature) throw new UnmatchedResponseFieldsException();
            }

            r.SenderHMAC = HMAC.Decode(reader);

            // verify PingRequestId32
            if (r.PingRequestId32 != optionalPingRequestPacketToCheckRequestId32.PingRequestId32)
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
        public static ushort DecodeToken16FromUdpPayloadData(byte[] udpPayloadData)
        { // first byte is packet type. then 4 bytes are P2pConnectionToken32
            return (ushort)(udpPayloadData[1] | (udpPayloadData[2] << 8));
        }

        public static LowLevelUdpResponseScanner GetScanner(P2pConnectionToken32 senderToken32, uint pingRequestId32)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.PingResponsePacket);
            GetHeaderFields(w, senderToken32, pingRequestId32);
            return new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
        }
        static void GetHeaderFields(BinaryWriter writer, P2pConnectionToken32 senderToken32, uint pingRequestId32)
        {
            senderToken32.Encode(writer);
            writer.Write(pingRequestId32);
        }

        public void GetSignedFieldsForSenderHMAC(BinaryWriter writer)
        {
            GetHeaderFields(writer, SenderToken32, PingRequestId32);
            writer.Write(Flags);
            if (P2pConnectionSetupSignature != null)
                P2pConnectionSetupSignature.Encode(writer);
        }
        const ushort P2pConnectionSetupSignature_MagicNumber = 0x7827;
        static void GetSignedFieldsForP2pConnectionSetupSignature(BinaryWriter writer, RegisterSynPacket registerSynPacket, RegisterSynAckPacket registerSynAckPacket)
        {
            registerSynPacket.RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(registerSynPacket.Timestamp32S);
            registerSynAckPacket.NeighborPublicKey.Encode(writer);
            writer.Write(P2pConnectionSetupSignature_MagicNumber);
        }
               
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpPacketType.PingResponsePacket);
            Flags = 0;
            if (P2pConnectionSetupSignature != null) Flags |= Flags_P2pConnectionSetupSignatureExists;
            GetSignedFieldsForSenderHMAC(writer);
            SenderHMAC.Encode(writer);
            return ms.ToArray();
        }

    }
}
