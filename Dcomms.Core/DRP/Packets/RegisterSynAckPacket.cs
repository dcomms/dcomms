using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// response to REGISTER SYN request, 
    /// is sent from neighbor/responder/N to M, from M to RP, from RP to requester/A
    /// ответ от N к A идет по тому же пути, узлы помнят обратный путь по RequestId
    /// </summary>
    public class RegisterSynAckPacket
    {
        public static byte Flag_RPtoA = 0x01; // set if packet is transmitted from RP to registering A, otherwise it is zero
        public static byte Flag_ipv6 = 0x02;  // set if responder is accessible via ipv6 address. default (0) means ipv4
        public byte Flags;

        P2pConnectionToken32 SenderToken32; // is not sent from RP to A
        RegistrationPublicKey RequesterPublicKey_RequestID; // public key of requester (A)
        /// <summary>
        /// against flood by this packet in future, without N (against replay attack)
        /// is copied from REGISTER SYN request packet by N and put into the SYN-ACK response
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint RegisterSynTimestamp32S;
        public DrpResponderStatusCode NeighborStatusCode;
        public EcdhPublicKey NeighborEcdhePublicKey;
        /// <summary>
        /// not null only for (status=connected) (N->X-M-RP-A)
        /// IP address of N with salt, encrypted for A
        /// 16 bytes for ipv4 address of neighbor, 32 bytes for ipv6
        /// </summary>
        public byte[] ToNeighborTxParametersEncrypted;


        public RegistrationPublicKey NeighborPublicKey; // public key of responder (neighbor, N)
        /// <summary>
        /// signs fields: {NeighborStatusCode,NeighborEcdhePublicKey,ToNeighborTxParametersEncrypted,RequesterPublicKey_RequestID,RegisterSynTimestamp32S,NeighborPublicKey }
        /// </summary>
        public RegistrationSignature NeighborSignature; 

        HMAC SenderHMAC; // is not sent from RP to A
        public IPEndPoint RequesterEndpoint; // is sent only from RP to A, to provide public IP:port of A, for UDP hole punching  // not signed, not encrypted
        public NextHopAckSequenceNumber16 NhaSeq16; // is not sent from RP to A // goes into NHA packet


        /// <summary>
        /// decodes the packet, decrypts ToNeighborTxParametersEncrypted, verifies NeighborSignature, verifies match to register SYN
        /// </summary>
        /// <param name="reader">is positioned after first byte = packet type</param>    
        public static RegisterSynAckPacket DecodeAndVerifyAtRequester(byte[] registerSynAckPacketData, RegisterSynPacket registerSyn, byte[] localEcdhPrivateKey, 
            ICryptoLibrary cryptoLibrary, out P2pStreamParameters localToRemoteTxParameters)
        {
            var reader = PacketProcedures.CreateBinaryReader(registerSynAckPacketData, 1);
            var r = new RegisterSynAckPacket();
            r.Flags = reader.ReadByte();
            if ((r.Flags & Flag_RPtoA) == 0) r.SenderToken32 = P2pConnectionToken32.Decode(reader);           
            if ((r.Flags & Flag_ipv6) != 0) throw new InvalidOperationException();
            r.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            r.RegisterSynTimestamp32S = reader.ReadUInt32();
            r.NeighborStatusCode = (DrpResponderStatusCode)reader.ReadByte();
            r.NeighborEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.ToNeighborTxParametersEncrypted = reader.ReadBytes(16);
            r.NeighborPublicKey = RegistrationPublicKey.Decode(reader);
            r.NeighborSignature = RegistrationSignature.DecodeAndVerify(reader, cryptoLibrary, w => r.GetCommonRequesterAndResponderFields(w, false, true), r.NeighborPublicKey);
            r.AssertMatchToRegisterSyn(registerSyn);
            
            localToRemoteTxParameters = P2pStreamParameters.DecryptAtRegisterRequester(localEcdhPrivateKey, registerSyn, r, cryptoLibrary);
            if ((r.Flags & Flag_RPtoA) != 0) r.RequesterEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
            if ((r.Flags & Flag_RPtoA) == 0) r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            // if ((flags & Flag_RPtoA) == 0)
            //     SenderHMAC = HMAC.Decode(reader);
            return r;
        }
        void AssertMatchToRegisterSyn(RegisterSynPacket registerSyn)
        {
            if (registerSyn.RequesterPublicKey_RequestID.Equals(this.RequesterPublicKey_RequestID) == false)
                throw new UnmatchedResponseFieldsException();
            if (registerSyn.Timestamp32S != this.RegisterSynTimestamp32S)
                throw new UnmatchedResponseFieldsException();
        }


        /// <summary>
        /// fields for neighbor's signature and for AEAD hash
        /// </summary>
        public void GetCommonRequesterAndResponderFields(BinaryWriter writer, bool includeSignature, bool includeTxParameters)
        {
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(RegisterSynTimestamp32S);
            writer.Write((byte)NeighborStatusCode);
            NeighborEcdhePublicKey.Encode(writer);
            if (includeTxParameters) writer.Write(ToNeighborTxParametersEncrypted);
            NeighborPublicKey.Encode(writer);
            if (includeSignature) NeighborSignature.Encode(writer);
        }

        /// <param name="txParametersToPeerNeighbor">is not null for packets between registered peers</param>
        public byte[] EncodeAtResponder(P2pStreamParameters txParametersToPeerNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterSynPacket);
            byte flags = 0;
            if (txParametersToPeerNeighbor != null) flags |= Flag_RPtoA;
            writer.Write(flags);
            if (txParametersToPeerNeighbor != null)
                txParametersToPeerNeighbor.RemotePeerToken32.Encode(writer);

            GetCommonRequesterAndResponderFields(writer, true, true);

            if (txParametersToPeerNeighbor != null)
            {
                throw new NotImplementedException();
                //   txParametersToPeerNeighbor.GetSharedHmac(cryptoLibrary, this.GetFieldsForSenderHmac).Encode(writer);
                NhaSeq16.Encode(writer);
            }
            else
            {
                PacketProcedures.EncodeIPEndPointIpv4(writer, RequesterEndpoint);
            }

            return ms.ToArray();

        }


        public static LowLevelUdpResponseScanner GetScanner(RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.RegisterSynAckPacket);
            w.Write((byte)0);
            requesterPublicKey_RequestID.Encode(w);
            w.Write(registerSynTimestamp32S);
            
            return new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                IgnoredByteAtOffset1 = 1 // ignore flags
            };
        }

    }
}
