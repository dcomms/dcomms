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
        public byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

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
        /// </summary>
        public byte[] ToResponderTxParametersEncrypted;
        public const int ToResponderTxParametersEncryptedLength = 32;


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

        public NextHopAckSequenceNumber16 NhaSeq16; // is not sent from EP to A (because response to the SYNACK is ACK, not NHACK) // goes into NHACK packet at peer that responds to this packet
        public byte[] OriginalUdpPayloadData;

        /// <summary>
        /// decodes the packet, decrypts ToNeighborTxParametersEncrypted, verifies NeighborSignature, verifies match to register SYN
        /// </summary>
        /// <param name="newConnectionToNeighborAtRequesterNullable">if not null (at requester) - this procedure verifies ResponderSignature</param>
        /// <param name="reader">is positioned after first byte = packet type</param>    
        public static RegisterSynAckPacket DecodeAndOptionallyVerify(byte[] registerSynAckPacketData, RegisterSynPacket synNullable,
            ConnectionToNeighbor newConnectionToNeighborAtRequesterNullable)
        {
            var reader = PacketProcedures.CreateBinaryReader(registerSynAckPacketData, 1);
            var synAck = new RegisterSynAckPacket();
            synAck.OriginalUdpPayloadData = registerSynAckPacketData;
            synAck.Flags = reader.ReadByte();
            if ((synAck.Flags & Flag_EPtoA) == 0) synAck.SenderToken32 = P2pConnectionToken32.Decode(reader);           
            if ((synAck.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            synAck.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            synAck.RegisterSynTimestamp32S = reader.ReadUInt32();
            synAck.ResponderStatusCode = (DrpResponderStatusCode)reader.ReadByte();
            synAck.ResponderEcdhePublicKey = EcdhPublicKey.Decode(reader);
            synAck.ToResponderTxParametersEncrypted = reader.ReadBytes(ToResponderTxParametersEncryptedLength);
            synAck.ResponderPublicKey = RegistrationPublicKey.Decode(reader);

            if (newConnectionToNeighborAtRequesterNullable != null)
            {
                synAck.ResponderSignature = RegistrationSignature.DecodeAndVerify(
                    reader, newConnectionToNeighborAtRequesterNullable.Engine.CryptoLibrary,
                    w => synAck.GetCommonRequesterProxierResponderFields(w, false, true),
                    synAck.ResponderPublicKey);
            }
            else
            { // at proxy we don't verify responder's signature, to avoid high spending of resources
                synAck.ResponderSignature = RegistrationSignature.Decode(reader);
            }

            if (synNullable != null)
            {
                synAck.AssertMatchToRegisterSyn(synNullable);
                if (newConnectionToNeighborAtRequesterNullable != null)
                {
                    newConnectionToNeighborAtRequesterNullable.Decrypt_synack_ToResponderTxParametersEncrypted_AtRequester_DeriveSharedDhSecret(synNullable, synAck);
                }
            }
            if ((synAck.Flags & Flag_EPtoA) != 0) synAck.RequesterEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
            if ((synAck.Flags & Flag_EPtoA) == 0)
            {
                synAck.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
                synAck.SenderHMAC = HMAC.Decode(reader);
            }
            return synAck;
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
            if (includeTxParameters)
            {
                if (ToResponderTxParametersEncrypted.Length != ToResponderTxParametersEncryptedLength) throw new ArgumentException();
                writer.Write(ToResponderTxParametersEncrypted);
            }
            ResponderPublicKey.Encode(writer);
            if (includeSignature) ResponderSignature.Encode(writer);
        }

        /// <param name="synReceivedFromInP2pMode">is not null for packets between registered peers</param>
        public byte[] EncodeAtResponder(ConnectionToNeighbor synReceivedFromInP2pMode)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterSynAck);
            byte flags = 0;
            if (synReceivedFromInP2pMode == null) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (synReceivedFromInP2pMode != null)
            {
                SenderToken32 = synReceivedFromInP2pMode.RemotePeerToken32;
                SenderToken32.Encode(writer);
            }

            GetCommonRequesterProxierResponderFields(writer, true, true);

            if (synReceivedFromInP2pMode != null)
            {
                NhaSeq16.Encode(writer);
                this.SenderHMAC = synReceivedFromInP2pMode.GetSenderHMAC(this.GetSignedFieldsForSenderHMAC);
                this.SenderHMAC.Encode(writer);
            }
            else
            {
                PacketProcedures.EncodeIPEndPoint(writer, RequesterEndpoint);
            }

            return ms.ToArray();

        }
        internal void GetSignedFieldsForSenderHMAC(BinaryWriter w)
        {
            SenderToken32.Encode(w);
            GetCommonRequesterProxierResponderFields(w, true, true);
            NhaSeq16.Encode(w);
        }

        /// <summary>
        /// creates a scanner that finds SYNACK that matches to SYN
        /// </summary>
        /// <param name="connectionToNeighborNullable">
        /// peer that responds to SYN with SYNACK
        /// if not null - the scanner will verify SYNACK.SenderHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S, ConnectionToNeighbor connectionToNeighborNullable = null)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.RegisterSynAck);
            w.Write((byte)0);
            if (connectionToNeighborNullable != null)
            {
                connectionToNeighborNullable.LocalRxToken32.Encode(w);
            }

            requesterPublicKey_RequestID.Encode(w);
            w.Write(registerSynTimestamp32S);
            
            var r = new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                IgnoredByteAtOffset1 = 1 // ignore flags
            };
            if (connectionToNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    var synack = DecodeAndOptionallyVerify(responseData, null, null);
                    if (synack.SenderHMAC.Equals(connectionToNeighborNullable.GetSenderHMAC(synack.GetSignedFieldsForSenderHMAC)) == false) return false;
                    return true;
                };
            }
            return r;
        }

    }
}
