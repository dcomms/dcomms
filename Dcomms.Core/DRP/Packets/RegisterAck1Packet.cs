using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// response to REGISTER REQ request, 
    /// is sent from neighbor/responder/N to M, from M to EP, from EP to requester/A
    /// ответ от N к A идет по тому же пути, узлы помнят обратный путь по RequestId
    /// </summary>
    public class RegisterAck1Packet
    {
        public static byte Flag_EPtoA = 0x01; // set if packet is transmitted from EP to registering A, otherwise it is zero
        public byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        NeighborToken32 NeighborToken32; // is not sent from EP to A
        public RegistrationId RequesterRegistrationId; // public key of requester (A)
        /// <summary>
        /// against flood by this packet in future, without N (against replay attack)
        /// is copied from REGISTER REQ request packet by N and put into the ACK1 response
        /// </summary>
        public Int64 ReqTimestamp64;
        public DrpResponderStatusCode ResponderStatusCode;
        public EcdhPublicKey ResponderEcdhePublicKey; // is not null only when ResponderStatusCode=confirmed
        /// <summary>
        /// not null only for (status=connected) (N->X-M-EP-A)
        /// IP address of N with salt, encrypted for A
        /// </summary>
        public byte[] ToResponderTxParametersEncrypted; // is not null only when ResponderStatusCode=confirmed
        public const int ToResponderTxParametersEncryptedLength = 32;


        public RegistrationId ResponderRegistrationId; // public key of responder (neighbor, N)
        /// <summary>
        /// signs fields:
        /// {
        ///   (virtual) REQ shared fields
        ///   RequesterRegistrationId,
        ///   RegisterReqTimestamp32S,
        ///   ResponderStatusCode,
        ///   ResponderEcdhePublicKey,
        ///   ToResponderTxParametersEncrypted,
        ///   ResponderPublicKey 
        /// }
        /// </summary>
        public RegistrationSignature ResponderSignature; 

        HMAC NeighborHMAC; // is not sent from EP to A

        /// <summary>
        /// is not null  only in EP-A   mode when ResponderStatusCode=confirmed
        /// </summary>       
        public IPEndPoint RequesterEndpoint; // public IP:port of A, for UDP hole punching  // not encrypted.  IP address is validated at requester side       

        public NeighborPeerAckSequenceNumber16 NpaSeq16; // is not sent from EP to A (because response to the ACK1 is ACK2, not NPACK) // goes into NPACK packet at peer that responds to this packet
        public byte[] DecodedUdpPayloadData;

        /// <summary>
        /// decodes the packet, decrypts ToNeighborTxParametersEncrypted, verifies NeighborSignature, verifies match to register REQ
        /// </summary>
        /// <param name="newConnectionToNeighborAtRequesterNullable">if not null (at requester) - this procedure verifies ResponderSignature</param>
        /// <param name="reader">is positioned after first byte = packet type</param>    
        public static RegisterAck1Packet DecodeAndOptionallyVerify(byte[] ack1UdpData, RegisterRequestPacket reqNullable,
            ConnectionToNeighbor newConnectionToNeighborAtRequesterNullable)
        {
            var reader = PacketProcedures.CreateBinaryReader(ack1UdpData, 1);
            var ack1 = new RegisterAck1Packet();
            ack1.DecodedUdpPayloadData = ack1UdpData;
            ack1.Flags = reader.ReadByte();
            if ((ack1.Flags & Flag_EPtoA) == 0) ack1.NeighborToken32 = NeighborToken32.Decode(reader);           
            if ((ack1.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            ack1.RequesterRegistrationId = RegistrationId.Decode(reader);
            ack1.ReqTimestamp64 = reader.ReadInt64();
            ack1.ResponderStatusCode = (DrpResponderStatusCode)reader.ReadByte();
            if (ack1.ResponderStatusCode == DrpResponderStatusCode.confirmed)
            {
                ack1.ResponderEcdhePublicKey = EcdhPublicKey.Decode(reader);
                ack1.ToResponderTxParametersEncrypted = reader.ReadBytes(ToResponderTxParametersEncryptedLength);
            }
            ack1.ResponderRegistrationId = RegistrationId.Decode(reader);

            if (newConnectionToNeighborAtRequesterNullable != null)
            {
                if (reqNullable == null) throw new ArgumentException();
                ack1.ResponderSignature = RegistrationSignature.DecodeAndVerify(
                    reader, newConnectionToNeighborAtRequesterNullable.Engine.CryptoLibrary,
                    w =>
                    {
                        reqNullable.GetSharedSignedFields(w, true);
                        ack1.GetSharedSignedFields(w, false, true);
                    },
                    ack1.ResponderRegistrationId);
            }
            else
            { // at proxy we don't verify responder's signature, to avoid high spending of resources
                ack1.ResponderSignature = RegistrationSignature.Decode(reader);
            }

            if (reqNullable != null)
            {
                ack1.AssertMatchToRegisterReq(reqNullable);
                if (newConnectionToNeighborAtRequesterNullable != null && ack1.ResponderStatusCode == DrpResponderStatusCode.confirmed)
                {
                    newConnectionToNeighborAtRequesterNullable.Decrypt_ack1_ToResponderTxParametersEncrypted_AtRequester_DeriveSharedDhSecret(reqNullable, ack1);
                }
            }
            if ((ack1.Flags & Flag_EPtoA) != 0 && ack1.ResponderStatusCode == DrpResponderStatusCode.confirmed)
                ack1.RequesterEndpoint = PacketProcedures.DecodeIPEndPoint(reader);
            if ((ack1.Flags & Flag_EPtoA) == 0)
            {
                ack1.NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);
                ack1.NeighborHMAC = HMAC.Decode(reader);
            }
            return ack1;
        }

        void AssertMatchToRegisterReq(RegisterRequestPacket req)
        {
            if (req.RequesterRegistrationId.Equals(this.RequesterRegistrationId) == false)
                throw new UnmatchedFieldsException();
            if (req.ReqTimestamp64 != this.ReqTimestamp64)
                throw new UnmatchedFieldsException();
        }


        /// <summary>
        /// fields for responder signature and for AEAD hash
        /// </summary>
        public void GetSharedSignedFields(BinaryWriter writer, bool includeSignature, bool includeTxParameters)
        {
            RequesterRegistrationId.Encode(writer);
            writer.Write(ReqTimestamp64);
            writer.Write((byte)ResponderStatusCode);
            if (ResponderStatusCode == DrpResponderStatusCode.confirmed)
            {
                ResponderEcdhePublicKey.Encode(writer);
                if (includeTxParameters)
                {
                    if (ToResponderTxParametersEncrypted.Length != ToResponderTxParametersEncryptedLength) throw new ArgumentException();
                    writer.Write(ToResponderTxParametersEncrypted);
                }
            }
            ResponderRegistrationId.Encode(writer);
            if (includeSignature) ResponderSignature.Encode(writer);
        }

        /// <param name="reqReceivedFromInP2pMode">is not null for packets between registered peers</param>
        public byte[] Encode_OpionallySignNeighborHMAC(ConnectionToNeighbor reqReceivedFromInP2pMode)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpDmpPacketTypes.RegisterAck1);
            byte flags = 0;
            if (reqReceivedFromInP2pMode == null) flags |= Flag_EPtoA;
            writer.Write(flags);
            if (reqReceivedFromInP2pMode != null)
            {
                NeighborToken32 = reqReceivedFromInP2pMode.RemoteNeighborToken32;
                NeighborToken32.Encode(writer);
            }

            GetSharedSignedFields(writer, true, true);

            if (reqReceivedFromInP2pMode != null)
            {
                NpaSeq16.Encode(writer);
                this.NeighborHMAC = reqReceivedFromInP2pMode.GetNeighborHMAC(this.GetSignedFieldsForNeighborHMAC);
                this.NeighborHMAC.Encode(writer);
            }
            else
            {
                PacketProcedures.EncodeIPEndPoint(writer, RequesterEndpoint);
            }

            return ms.ToArray();

        }
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter w)
        {
            NeighborToken32.Encode(w);
            GetSharedSignedFields(w, true, true);
            NpaSeq16.Encode(w);
        }

        /// <summary>
        /// creates a scanner that finds ACK1 that matches to REQ
        /// </summary>
        /// <param name="connectionToNeighborNullable">
        /// peer that responds to REQ with ACK1
        /// if not null - the scanner will verify ACK1.NeighborHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(RegistrationId requesterPublicKey_RequestID, Int64 registerReqTimestamp64, ConnectionToNeighbor connectionToNeighborNullable = null)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.RegisterAck1);
            w.Write((byte)0);
            if (connectionToNeighborNullable != null)
            {
                connectionToNeighborNullable.LocalNeighborToken32.Encode(w);
            }

            requesterPublicKey_RequestID.Encode(w);
            w.Write(registerReqTimestamp64);
            
            var r = new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                IgnoredByteAtOffset1 = 1 // ignore flags
            };
            if (connectionToNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    var ack1 = DecodeAndOptionallyVerify(responseData, null, null);
                    if (ack1.NeighborHMAC.Equals(connectionToNeighborNullable.GetNeighborHMAC(ack1.GetSignedFieldsForNeighborHMAC)) == false) return false;
                    return true;
                };
            }
            return r;
        }

    }
}
