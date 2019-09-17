using Dcomms.Cryptography;
using Dcomms.DMP;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    public class SessionDescription
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
        /// signs fields 
        /// {
        ///   shared REQ, SYNACK fields
        ///   UserCertificate,
        ///   DirectChannelEndPoint
        /// }
        /// </summary>
        public UserCertificateSignature UserCertificateSignature { get; set; }

        internal void WriteSignedFields(BinaryWriter w)
        {
            PacketProcedures.EncodeIPEndPoint(w, DirectChannelEndPoint);
        }

  
        /// <param name="ack1SdIsReady">
        /// =true for SD in ACK2
        /// =false for SD in ACK1 (since the SessionDescription is not initialized yet)
        /// </param>
        internal byte[] Encrypt(ICryptoLibrary cryptoLibrary, InviteRequestPacket req, InviteAck1Packet ack1, Session session,
            bool ack1SdIsReady
            )
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write(Flags);
            UserCertificate.Encode(w);
            PacketProcedures.EncodeIPEndPoint(w, DirectChannelEndPoint);
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
            PacketProcedures.CreateBinaryWriter(out var ms2, out var w2);
            req.GetSharedSignedFields(w2);
            ack1.GetSharedSignedFields(w2, ack1SdIsReady);            
            var iv = cryptoLibrary.GetHashSHA256(ms2.ToArray()).Take(16).ToArray();
            ms2.Write(session.SharedDhSecret, 0, session.SharedDhSecret.Length);
            var aesKey = cryptoLibrary.GetHashSHA256(ms2.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion

            cryptoLibrary.ProcessAesCbcBlocks(true, aesKey, iv, plainTextSdData, encryptedSdData);
          
            return encryptedSdData;
        }
       
        /// <param name="receivedFromUser">comes from local contact book</param>
        internal static SessionDescription Decrypt_Verify(ICryptoLibrary cryptoLibrary, byte[] encryptedSdData, 
            InviteRequestPacket req,
            InviteAck1Packet ack1,
            bool ack1SdIsReady,
            Session session,
            UserId receivedFromUser,
            DateTime localTimeNowUtc
            )
        {
            #region key, iv
            PacketProcedures.CreateBinaryWriter(out var ms2, out var w2);
            req.GetSharedSignedFields(w2);
            ack1.GetSharedSignedFields(w2, ack1SdIsReady);
            var iv = cryptoLibrary.GetHashSHA256(ms2.ToArray()).Take(16).ToArray();
            ms2.Write(session.SharedDhSecret, 0, session.SharedDhSecret.Length);
            var aesKey = cryptoLibrary.GetHashSHA256(ms2.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp
            #endregion

            // decrypt
            var plainTextSdData = new byte[encryptedSdData.Length];
            cryptoLibrary.ProcessAesCbcBlocks(false, aesKey, iv, encryptedSdData, plainTextSdData);

            var r = new SessionDescription();
            var reader = PacketProcedures.CreateBinaryReader(plainTextSdData, 0);
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.UserCertificate = UserCertificate.Decode_AssertIsValidNow(reader, cryptoLibrary, receivedFromUser, localTimeNowUtc);
            r.DirectChannelEndPoint = PacketProcedures.DecodeIPEndPoint(reader);
            r.UserCertificateSignature = UserCertificateSignature.DecodeAndVerify(reader, cryptoLibrary, 
                w =>
                {
                    req.GetSharedSignedFields(w);
                    ack1.GetSharedSignedFields(w, true);
                    r.WriteSignedFields(w);
                },
                r.UserCertificate);
                       
            return r;
        }
    }
}
 