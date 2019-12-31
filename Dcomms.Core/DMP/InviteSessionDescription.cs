using Dcomms.Cryptography;
using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.DMP
{
    public class InviteSessionDescription
    {
        byte Flags; // will include type = DMP/WebRTP SDP
        const byte FlagsMask_MustBeZero = 0b11110000;

        /// <summary>
        /// certificate of user who generated instance of this session description
        /// </summary>
        public UserCertificate UserCertificate { get; set; }

       
        /// <summary>
        /// at user who declared this SessionDescription: IP address and UDP port to receive DMP data from remote side via direct channel
        /// at user who received this SessionDescription (via INVITE): IP address and UDP port to send DMP data send via direct channel to remote party
        /// </summary>
        public IPEndPoint DirectChannelEndPoint;
        /// <summary>
        /// at user who declared this SessionDescription: local NAT behaviour model, comes from DrpEngine.LocalNatBehaviour
        /// at user who received this SessionDescription (via INVITE): NAT behaviour model at remote party
        /// </summary>
        public NatBehaviourModel NatBehaviour;
        public DirectChannelToken32 DirectChannelToken32;
        public SessionType SessionType;

        /// <summary>
        /// signs fields 
        /// {
        ///   shared REQ, ACK1 fields
        ///   UserCertificate,
        ///   DirectChannelEndPoint
        /// }
        /// </summary>
        public UserCertificateSignature UserCertificateSignature { get; set; }

        public override string ToString() => $"dsEP={DirectChannelEndPoint}, sessionType={SessionType}, DirectChannelToken32={DirectChannelToken32}";

        internal void WriteSignedFields(BinaryWriter w)
        {
            BinaryProcedures.EncodeIPEndPoint(w, DirectChannelEndPoint);
            NatBehaviour.Encode(w);
            DirectChannelToken32.Encode(w);
            w.Write((byte)SessionType);
        }

  
        /// <param name="ack1SdIsReady">
        /// =true for SD in ACK2
        /// =false for SD in ACK1 (since the SessionDescription is not initialized yet)
        /// </param>
        internal byte[] Encrypt(ICryptoLibrary cryptoLibrary, InviteRequestPacket req, InviteAck1Packet ack1, InviteSession session,
            bool ack1SdIsReady
            )
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write(Flags);
            UserCertificate.Encode(w);
            BinaryProcedures.EncodeIPEndPoint(w, DirectChannelEndPoint);
            NatBehaviour.Encode(w);
            DirectChannelToken32.Encode(w);
            w.Write((byte)SessionType);
            UserCertificateSignature.Encode(w);
            var bytesInLastBlock = (int)ms.Position % CryptoLibraries.AesBlockSize;
            if (bytesInLastBlock != 0)
            {
                var bytesRemainingTillFullAesBlock = CryptoLibraries.AesBlockSize - bytesInLastBlock;
                w.Write(cryptoLibrary.GetRandomBytes(bytesRemainingTillFullAesBlock));
            }
            var plainTextSdData = ms.ToArray();
            var encryptedSdData = new byte[plainTextSdData.Length];

            #region key, iv
            BinaryProcedures.CreateBinaryWriter(out var ms2, out var w2);
            req.GetSharedSignedFields(w2);
            ack1.GetSharedSignedFields(w2, ack1SdIsReady);            
            var iv = cryptoLibrary.GetHashSHA256(ms2.ToArray()).Take(16).ToArray();
            ms2.Write(session.SharedInviteAckDhSecret, 0, session.SharedInviteAckDhSecret.Length);
            var aesKey = cryptoLibrary.GetHashSHA256(ms2.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion

            cryptoLibrary.ProcessAesCbcBlocks(true, aesKey, iv, plainTextSdData, encryptedSdData);
          
            return encryptedSdData;
        }
       
        /// <param name="receivedFromUser">comes from local contact book. is null if it is contact invitation</param>
        internal static InviteSessionDescription Decrypt_Verify(ICryptoLibrary cryptoLibrary, byte[] encryptedSdData, 
            InviteRequestPacket req,
            InviteAck1Packet ack1,
            bool ack1SdIsReady,
            InviteSession session,
            UserId receivedFromUserNullable,
            DateTime localTimeNowUtc
            )
        {
            #region key, iv
            BinaryProcedures.CreateBinaryWriter(out var ms2, out var w2);
            req.GetSharedSignedFields(w2);
            ack1.GetSharedSignedFields(w2, ack1SdIsReady);
            var iv = cryptoLibrary.GetHashSHA256(ms2.ToArray()).Take(16).ToArray();
            ms2.Write(session.SharedInviteAckDhSecret, 0, session.SharedInviteAckDhSecret.Length);
            var aesKey = cryptoLibrary.GetHashSHA256(ms2.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion

            // decrypt
            var plainTextSdData = new byte[encryptedSdData.Length];
            cryptoLibrary.ProcessAesCbcBlocks(false, aesKey, iv, encryptedSdData, plainTextSdData);

            var r = new InviteSessionDescription();
            var reader = BinaryProcedures.CreateBinaryReader(plainTextSdData, 0);
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.UserCertificate = UserCertificate.Decode_AssertIsValidNow(reader, cryptoLibrary, receivedFromUserNullable, localTimeNowUtc);
            r.DirectChannelEndPoint = BinaryProcedures.DecodeIPEndPoint(reader);
            r.NatBehaviour = NatBehaviourModel.Decode(reader);
            r.DirectChannelToken32 = DirectChannelToken32.Decode(reader);
            r.SessionType = (SessionType)reader.ReadByte();
            r.UserCertificateSignature = UserCertificateSignature.DecodeAndVerify(reader, cryptoLibrary, 
                w =>
                {
                    req.GetSharedSignedFields(w);
                    ack1.GetSharedSignedFields(w, ack1SdIsReady);
                    r.WriteSignedFields(w);
                },
                r.UserCertificate);                       
            return r;
        }
    }
    /// <summary>
    /// describes type of communication within direct channel which is being set up by INVITE packets
    /// gets encrypted in "SessionDescription"
    /// responder sends SessionDescription offer;  requester sends SessionDescription answer
    /// </summary>
    public enum SessionType
    {
        technicalMessages = 0,
        asyncShortSingleMessage = 1, // 3 packets: msgstart, msgack, msgpart(final)
        ike1 = 2, // 6 packets: 1) same as asyncShortSingleMessage from INVITE requester to responder, with IKE1 data; 2) same as asyncShortSingleMessage from INVITE responder to requester, with IKE1 data

        //  realtimeLongMessages,

        //  mixedMessages, // ?????????????????

        //   realtimeVoice,
        //   realtimeVideoAndVoice,
        //    getStructuredData_Posts, // like instagram
        //    getStructuredData_Reviews, 
        //   getStructuredData_GenericJSON, 

        //    getStaticPanelHtml, // an organization/chatbot  during techsupport sends offer to user with options what to do (like press 1.. press 2)
    }
    /// <summary>
    /// is generated by remote peer; token of local peer at remote peer
    /// </summary>
    public class DirectChannelToken32
    {
        public uint Token32;
        public ushort Token16 => (ushort)(Token32 & 0x0000FFFF);
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Token32);
        }
        public static DirectChannelToken32 Decode(BinaryReader reader)
        {
            var r = new DirectChannelToken32();
            r.Token32 = reader.ReadUInt32();
            return r;
        }
        public override bool Equals(object obj)
        {
            return ((DirectChannelToken32)obj).Token32 == this.Token32;
        }
        public override string ToString() => Token32.ToString("X8");
    }
}
 