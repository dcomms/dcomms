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
    /// is sent from neighbor/responder/N to M, from M to EP, from EP to requester/A
    /// ответ от N к A идет по тому же пути, узлы помнят обратный путь по RequestId
    /// </summary>
    public class RegisterSynAckPacket
    {
        public static byte Flag_EPtoA = 0x01; // set if packet is transmitted from EP to registering A, otherwise it is zero
        public static byte Flag_ipv6 = 0x02;  // set if responder is accessible via ipv6 address. default (0) means ipv4
        public byte Flags;

        P2pConnectionToken32 SenderToken32; // is not sent from EP to A
        public RegistrationPublicKey RequesterPublicKey_RequestID; // public key of requester (A)
        /// <summary>
        /// against flood by this packet in future, without N (against replay attack)
        /// is copied from REGISTER SYN request packet by N and put into the SYN-ACK response
        /// seconds since 2019-01-01 UTC, 32 bits are enough for 136 years
        /// </summary>
        public uint RegisterSynTimestamp32S;
        public DrpResponderStatusCode ResponderStatusCode;
        public EcdhPublicKey ResponderEcdhePublicKey;
        /// <summary>
        /// not null only for (status=connected) (N->X-M-EP-A)
        /// IP address of N with salt, encrypted for A
        /// 16 bytes for ipv4 address of neighbor, 32 bytes for ipv6
        /// </summary>
        public byte[] ToResponderTxParametersEncrypted;


        public RegistrationPublicKey ResponderPublicKey; // public key of responder (neighbor, N)
        /// <summary>
        /// signs fields:
        /// {
        ///   RequesterPublicKey_RequestID,
        ///   RegisterSynTimestamp32S,
        ///   ResponderStatusCode,
        ///   ResponderEcdhePublicKey,
        ///   ToResponderTxParametersEncrypted,
        ///   ResponderPublicKey 
        /// }
        /// </summary>
        public RegistrationSignature ResponderSignature; 

        HMAC SenderHMAC; // is not sent from EP to A
        
        #region is sent only from EP to A
        
        public IPEndPoint RequesterEndpoint; // public IP:port of A, for UDP hole punching  // not encrypted.  IP address is validated at requester side
       
        #endregion

        public NextHopAckSequenceNumber16 NhaSeq16; // is not sent from EP to A // goes into NHACK packet


        /// <summary>
        /// decodes the packet, decrypts ToNeighborTxParametersEncrypted, verifies NeighborSignature, verifies match to register SYN
        /// </summary>
        /// <param name="reader">is positioned after first byte = packet type</param>    
        public static RegisterSynAckPacket DecodeAndVerifyAtRequester(byte[] registerSynAckPacketData, RegisterSynPacket localRegisterSyn, ConnectionToNeighbor connectionToNeighbor)
        {
            var reader = PacketProcedures.CreateBinaryReader(registerSynAckPacketData, 1);
            var registerSynAck = new RegisterSynAckPacket();
            registerSynAck.Flags = reader.ReadByte();
            if ((registerSynAck.Flags & Flag_EPtoA) == 0) registerSynAck.SenderToken32 = P2pConnectionToken32.Decode(reader);           
            if ((registerSynAck.Flags & Flag_ipv6) != 0) throw new InvalidOperationException();
            registerSynAck.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            registerSynAck.RegisterSynTimestamp32S = reader.ReadUInt32();
            registerSynAck.ResponderStatusCode = (DrpResponderStatusCode)reader.ReadByte();
            registerSynAck.ResponderEcdhePublicKey = EcdhPublicKey.Decode(reader);
            registerSynAck.ToResponderTxParametersEncrypted = reader.ReadBytes(16);
            registerSynAck.ResponderPublicKey = RegistrationPublicKey.Decode(reader);
            registerSynAck.ResponderSignature = RegistrationSignature.DecodeAndVerify(reader, connectionToNeighbor.Engine.CryptoLibrary, w => registerSynAck.GetCommonRequesterProxierResponderFields(w, false, true), registerSynAck.ResponderPublicKey);
            registerSynAck.AssertMatchToRegisterSyn(localRegisterSyn);

            connectionToNeighbor.DecryptAtRegisterRequester(localRegisterSyn, registerSynAck);
            if ((registerSynAck.Flags & Flag_EPtoA) != 0) registerSynAck.RequesterEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
            if ((registerSynAck.Flags & Flag_EPtoA) == 0) registerSynAck.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            // if ((flags & Flag_EPtoA) == 0)
            //     SenderHMAC = HMAC.Decode(reader);
            return registerSynAck;
        }
        void AssertMatchToRegisterSyn(RegisterSynPacket localRegisterSyn)
        {
            if (localRegisterSyn.RequesterPublicKey_RequestID.Equals(this.RequesterPublicKey_RequestID) == false)
                throw new UnmatchedFieldsException();
            if (localRegisterSyn.Timestamp32S != this.RegisterSynTimestamp32S)
                throw new UnmatchedFieldsException();
        }


        /// <summary>
        /// fields for responder signature and for AEAD hash
        /// </summary>
        public void GetCommonRequesterProxierResponderFields(BinaryWriter writer, bool includeSignature, bool includeTxParameters)
        {
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(RegisterSynTimestamp32S);
            writer.Write((byte)ResponderStatusCode);
            ResponderEcdhePublicKey.Encode(writer);
            if (includeTxParameters) writer.Write(ToResponderTxParametersEncrypted);
            ResponderPublicKey.Encode(writer);
            if (includeSignature) ResponderSignature.Encode(writer);
        }

        /// <param name="connectionToNeighbor">is not null for packets between registered peers</param>
        public byte[] EncodeAtResponder(ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterSynAck);
            byte flags = 0;
            if (connectionToNeighbor == null) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (connectionToNeighbor != null)
                connectionToNeighbor.RemotePeerToken32.Encode(writer);

            GetCommonRequesterProxierResponderFields(writer, true, true);

            if (connectionToNeighbor != null)
            {
                throw new NotImplementedException();
                //   txParametersToPeerNeighbor.GetSharedHmac(cryptoLibrary, this.GetFieldsForSenderHmac).Encode(writer);
                NhaSeq16.Encode(writer);
            }
            else
            {
                PacketProcedures.EncodeIPEndPoint(writer, RequesterEndpoint);
            }

            return ms.ToArray();

        }


        public static LowLevelUdpResponseScanner GetScanner(RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.RegisterSynAck);
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
