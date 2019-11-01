using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->EP->M->N
    /// peers remember the register request operation by RequesterRegistrationId 
    /// peers have the processed request already authorized, while processing this packet
    /// </summary>
    public class RegisterAck2Packet
    {
        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32 in case when this packet goes ofver established P2P connection (flag A-EP is zero)
        /// </summary>
        public NeighborToken32 NeighborToken32;
        /// <summary>
        /// 1: if packet is transmitted from registering A to EP, 
        /// 0: if packet is transmitted between neighbor peers (from sender to receiver). NeighborHMAC is sent 
        /// </summary>
        static byte Flag_AtoEP = 0x01;
        public static byte Flag_ipv6 = 0x02;  // set if requester is accessible via ipv6 address. default (0) means ipv4
        public byte Flags;
        public bool AtoEP => (Flags & Flag_AtoEP) != 0;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public Int64 ReqTimestamp64;
        public RegistrationId RequesterRegistrationId;
        /// <summary>
        /// IP address of A + UDP port + salt     
        /// goes into N->A p2pStreamParameters
        /// </summary>
        public byte[] ToRequesterTxParametersEncrypted;
        public const int ToRequesterTxParametersEncryptedLength = 32;
        /// <summary>
        /// signs fields: {
        ///  shared REQ, ACK1 fields
        ///  RequesterRegistrationId,RegisterReqTimestamp32S,ToRequesterTxParametersEncrypted 
        /// }
        /// is verified by N (responder)
        /// </summary>
        public RegistrationSignature RequesterSignature;

        public HMAC NeighborHMAC; // is NULL for A->EP
        public RequestP2pSequenceNumber16 ReqP2pSeq16;
        public byte[] DecodedUdpPayloadData;
        public RegisterAck2Packet()
        {
        }
        /// <param name="connectionToNeighborNullable">
        /// peer that sends ACK2
        /// if not null - the scanner will verify ACK2.NeighborHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(Logger logger, ConnectionToNeighbor connectionToNeighborNullable, RegisterRequestPacket req)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)PacketTypes.RegisterAck2);
          
            writer.Write((byte)0); // ignored flags

            if (connectionToNeighborNullable != null)
            {
                connectionToNeighborNullable.LocalNeighborToken32.Encode(writer);
            }

            writer.Write(req.ReqTimestamp64);
            req.RequesterRegistrationId.Encode(writer);

            var r = new LowLevelUdpResponseScanner
            {
                IgnoredByteAtOffset1 = 1,
                ResponseFirstBytes = ms.ToArray()
            };
            if (connectionToNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    if (connectionToNeighborNullable.IsDisposed)
                    {
                        logger.WriteToLog_needsAttention("ignoring ACK2: connection is disposed");
                        return false;
                    }
                    var ack2 = Decode_OptionallyVerify_InitializeP2pStreamAtResponder(responseData, null, null, null);
                    if (ack2.NeighborHMAC.Equals(connectionToNeighborNullable.GetNeighborHMAC(ack2.GetSignedFieldsForNeighborHMAC)) == false)
                    {
                        logger.WriteToLog_attacks("ignoring ACK2: received NeighborHMAC is invalid");
                        return false;
                    }
                    return true;
                };
            }


            return r;
        }

        /// <param name="connectionToNeighbor">is null for A->EP mode</param>
        public byte[] Encode_OptionallySignNeighborHMAC(ConnectionToNeighbor connectionToNeighborNullable)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            writer.Write((byte)PacketTypes.RegisterAck2);
            byte flags = 0;
            if (connectionToNeighborNullable == null) flags |= Flag_AtoEP;
            writer.Write(flags);

            if (connectionToNeighborNullable != null)
            {
                NeighborToken32 = connectionToNeighborNullable.RemoteNeighborToken32;
                NeighborToken32.Encode(writer);
            }

            GetSharedSignedFields(writer, true, true);

            ReqP2pSeq16.Encode(writer);

            if (connectionToNeighborNullable != null)
                connectionToNeighborNullable.GetNeighborHMAC(this.GetSignedFieldsForNeighborHMAC).Encode(writer);
            
            return ms.ToArray();
        }
        /// <summary>
        /// used for signature at requester; as source for p2p stream AEAD hash
        /// </summary>
        public void GetSharedSignedFields(BinaryWriter writer, bool includeRequesterSignature, bool includeTxParameters)
        {
            writer.Write(ReqTimestamp64);
            RequesterRegistrationId.Encode(writer);
            if (includeTxParameters) writer.Write(ToRequesterTxParametersEncrypted);
            if (includeRequesterSignature) RequesterSignature.Encode(writer);
        }
        
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter writer)
        {
            NeighborToken32.Encode(writer);
            GetSharedSignedFields(writer, true, true);
            ReqP2pSeq16.Encode(writer);
        }

        void AssertMatchToSyn(RegisterRequestPacket remoteRegisterReq)
        {
            if (!this.RequesterRegistrationId.Equals(remoteRegisterReq.RequesterRegistrationId))
                throw new UnmatchedFieldsException();
            if (this.ReqTimestamp64 != remoteRegisterReq.ReqTimestamp64)
                throw new UnmatchedFieldsException();
        }

        /// <param name="newConnectionAtResponderToRequesterNullable">
        /// direct P2P stream from N to A
        /// if newConnectionAtResponderToRequesterNullable is specified, the procedure 
        /// verifies RequesterHMAC, decrypts endpoint of A (ToRequesterTxParametersEncrypted), initializes P2P stream
        /// </param>
        public static RegisterAck2Packet Decode_OptionallyVerify_InitializeP2pStreamAtResponder(byte[] registerAckPacketData,
            RegisterRequestPacket reqNullable,
            RegisterAck1Packet ack1Nullable,
            ConnectionToNeighbor newConnectionAtResponderToRequesterNullable
          )
        {
            var reader = PacketProcedures.CreateBinaryReader(registerAckPacketData, 1);

            var ack = new RegisterAck2Packet();
            ack.DecodedUdpPayloadData = registerAckPacketData;
            ack.Flags = reader.ReadByte();
            if ((ack.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            if ((ack.Flags & Flag_AtoEP) == 0) ack.NeighborToken32 = NeighborToken32.Decode(reader);

            ack.ReqTimestamp64 = reader.ReadInt64();
            ack.RequesterRegistrationId = RegistrationId.Decode(reader);
            if (reqNullable != null) ack.AssertMatchToSyn(reqNullable);

            ack.ToRequesterTxParametersEncrypted = reader.ReadBytes(ToRequesterTxParametersEncryptedLength);
            if (newConnectionAtResponderToRequesterNullable != null)
            {
                newConnectionAtResponderToRequesterNullable.Decrypt_ack2_ToRequesterTxParametersEncrypted_AtResponder_InitializeP2pStream(reqNullable, ack1Nullable, ack);
            }

            ack.RequesterSignature = RegistrationSignature.Decode(reader);
            if (newConnectionAtResponderToRequesterNullable != null)
            {
                if (reqNullable == null) throw new ArgumentException();
                if (ack1Nullable == null) throw new ArgumentException();
                if (!ack.RequesterSignature.Verify(newConnectionAtResponderToRequesterNullable.Engine.CryptoLibrary,
                    w =>
                    {
                        reqNullable.GetSharedSignedFields(w, true);
                        ack1Nullable.GetSharedSignedFields(w, true, true);
                        ack.GetSharedSignedFields(w, false, true);
                    },
                    reqNullable.RequesterRegistrationId))
                    throw new BadSignatureException();
            }

            ack.ReqP2pSeq16 = RequestP2pSequenceNumber16.Decode(reader);

            if ((ack.Flags & Flag_AtoEP) == 0)
            {
                ack.NeighborHMAC = HMAC.Decode(reader); // is verified by Filter
            }
                       
            return ack;
        }
    }
}
