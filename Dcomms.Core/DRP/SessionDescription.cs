using Dcomms.Cryptography;
using Dcomms.DMP;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    class SessionDescription
    {
        byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public uint SignedTimestamp32S { get; set; }

        /// <summary>
        /// certificate of user who generated instance of this session description
        /// </summary>
        public UserCertificate UserCertificate { get; set; }

        /// <summary>
        /// at user who declared this SessionDescription: AES key to decrypt DMP data that is received from remote side via direct channel
        /// at user who received this SessionDescription (via INVITE): AES key to encrypt DMP data and send via direct channel
        /// </summary> 
        public byte[] DirectChannelAesKeyStage1 { get; set; }
        public byte[] DirectChannelAesKeyStage2 { get; set; } // gets derived from aesKeyStage1 when INVITE syn,synack,ack packets handshaking is completed; with inviteSession.SharedEcdhKey

        /// <summary>
        /// at user who declared this SessionDescription: IP address and UDP port to receive DMP data from remote side via direct channel
        /// at user who received this SessionDescription (via INVITE): IP address and UDP port to send DMP data send via direct channel to remote party
        /// </summary>
        public IPEndPoint DirectChannelEndPoint;


        /// <summary>
        /// signs fields 
        /// {
        ///   SignedTimestamp32S,
        ///   UserCertificate,
        ///   DirectChannelAesKeyStage1,
        ///   DirectChannelEndPoint
        /// }
        /// </summary>
        public UserCertificateSignature UserCertificateSignature { get; set; }



        byte[] Encrypt()
        {
            throw new NotImplementedException();
        }
        static SessionDescription Decrypt_Verify(ICryptoLibrary cryptoLibrary, byte[] data, UserID_PublicKeys receivedfromUser, DateTime timeNowUTC)
        {
            throw new NotImplementedException();

        }

    }
}
 