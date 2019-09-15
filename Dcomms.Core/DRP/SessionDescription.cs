using Dcomms.Cryptography;
using Dcomms.DMP;
using System;
using System.Collections.Generic;
using System.IO;
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
        ///   shared SYN, SYNACK fields
        ///   UserCertificate,
        ///   DirectChannelEndPoint
        /// }
        /// </summary>
        public UserCertificateSignature UserCertificateSignature { get; set; }

        internal void WriteSignedFields(BinaryWriter w)
        {
            PacketProcedures.EncodeIPEndPoint(w, DirectChannelEndPoint);
        }

        internal byte[] Encrypt(ICryptoLibrary cryptoLibrary)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write(Flags);
            UserCertificate.Encode(w);
            PacketProcedures.EncodeIPEndPoint(w, DirectChannelEndPoint);
            UserCertificateSignature.Encode(w);
            var plainTextSdData = ms.ToArray();
            var encryptedSdData = new byte[plainTextSdData.Length];

            cryptoLibrary.ProcessSingleAesBlock(true, x, x, plainTextSdData, encryptedSdData);
            // many blocks?????

            return encryptedSdData;
        }
        internal static SessionDescription Decrypt_Verify(ICryptoLibrary cryptoLibrary, byte[] data, UserID_PublicKeys receivedfromUser)
        {
            throw new NotImplementedException();

        }
    }
}
 