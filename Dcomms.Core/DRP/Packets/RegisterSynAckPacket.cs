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

        // RemotePeerToken16 SenderToken16; // is not sent from RP to A
        public DrpResponderStatusCode NeighborStatusCode;
        public EcdhPublicKey NeighborEcdhePublicKey;
        /// <summary>
        /// not null only for (status=connected) (N->X-M-RP-A)
        /// IP address of N with salt, encrypted for A
        /// 16 bytes for ipv4 address of neighbor, 32 bytes for ipv6
        /// </summary>
        public byte[] ToNeighborTxParametersEncrypted;

        RegistrationPublicKey RequesterPublicKey_RequestID; // public key of requester (A)
        /// <summary>
        /// against flood by this packet in future, without N (against replay attack)
        /// is copied from REGISTER SYN request packet by N and put into the SYN-ACK response
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint RegisterSynTimestamp32S;

        public RegistrationPublicKey NeighborPublicKey; // public key of responder (neighbor, N)
        public RegistrationSignature NeighborSignature; // {NeighborStatusCode,NeighborEndpoint_EncryptedByRequesterPublicKey,RequesterPublicKey_RequestID,RegisterSynTimestamp32S,NeighborPublicKey }
       
        HMAC SenderHMAC; // is not sent from RP to A
        public IPEndPoint RequesterEndpoint; // is sent only from RP to A to provide public IP:port of A

        /// <summary>
        /// decodes the packet, decrypts NeighborEndpoint_EncryptedByRequesterPublicKey, verifies NeighborSignature
        /// </summary>
        /// <param name="reader">is positioned after first byte = packet type</param>
        public static RegisterSynAckPacket DecodeAtRequester(BinaryReader reader, RegisterSynPacket registerSyn, byte[] localEcdhPrivateKey, 
            ICryptoLibrary cryptoLibrary, out P2pStreamParameters txParameters)
        {
            var r = new RegisterSynAckPacket();
            r.Flags = reader.ReadByte();
            if ((r.Flags & Flag_RPtoA) == 0) throw new InvalidOperationException();  // SenderToken16 = RemotePeerToken16.Decode(reader);
            if ((r.Flags & Flag_ipv6) != 0) throw new InvalidOperationException();
            r.NeighborStatusCode = (DrpResponderStatusCode)reader.ReadByte();
            r.NeighborEcdhePublicKey = EcdhPublicKey.Decode(reader);
            r.ToNeighborTxParametersEncrypted = reader.ReadBytes(16);
            r.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            r.RegisterSynTimestamp32S = reader.ReadUInt32();
            r.NeighborPublicKey = RegistrationPublicKey.Decode(reader);
            r.NeighborSignature = RegistrationSignature.Decode(reader);

            txParameters = P2pStreamParameters.DecryptAtRegisterRequester(localEcdhPrivateKey, registerSyn, r, cryptoLibrary);

            r.RequesterEndpoint = PacketProcedures.DecodeIPEndPoint(reader);

            //  xx

            // if ((flags & Flag_RPtoA) == 0)
            //     SenderHMAC = HMAC.Decode(reader);
            return r;
        }

        public void GetCommonRequesterAndResponderFields(BinaryWriter writer)
        {
            writer.Write((byte)NeighborStatusCode);
            NeighborEcdhePublicKey.Encode(writer);
            writer.Write(ToNeighborTxParametersEncrypted);
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(RegisterSynTimestamp32S);
            NeighborPublicKey.Encode(writer);
            NeighborSignature.Encode(writer);
        }
    }
}
