using Dcomms.Cryptography;
using Dcomms.DMP.Packets;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dcomms.DMP
{
    /// <summary>
    /// stores IV and AES key;
    /// encrypts and decrypts messages
    /// </summary>
    class MessageSession
    {
        public MessageSessionStatusCode Status { get; set; } = MessageSessionStatusCode.created;

        byte[] _iv, _aesKey;
        internal byte[] AesKey => _aesKey;
        internal void DeriveKeys(ICryptoLibrary cryptoLibrary, byte[] sharedPingPongHmacKey, MessageStartPacket messageStart,
            byte[] directChannelSharedDhSecret)
        {
            if (Status != MessageSessionStatusCode.created) throw new InvalidOperationException();

            BinaryProcedures.CreateBinaryWriter(out var msE, out var wE);
            messageStart.GetSignedFieldsForMessageHMAC(wE, false);
            wE.Write(sharedPingPongHmacKey);
            _iv = cryptoLibrary.GetHashSHA256(msE.ToArray()).Take(16).ToArray();
            wE.Write(directChannelSharedDhSecret);
            _aesKey = cryptoLibrary.GetHashSHA256(msE.ToArray());
            
            Status = MessageSessionStatusCode.inProgress;
        }


        internal byte[] EncryptShortSingleMessage(ICryptoLibrary cryptoLibrary, string messageText)
        {
            if (Status != MessageSessionStatusCode.inProgress) throw new InvalidOperationException();

            var decryptedMessageData = MessageEncoderDecoder.EncodePlainTextMessageWithPadding_plainTextUtf8_256(cryptoLibrary, messageText);          
            var encryptedMessageData = new byte[decryptedMessageData.Length];
           
            cryptoLibrary.ProcessAesCbcBlocks(true, _aesKey, _iv, decryptedMessageData, encryptedMessageData);

            Status = MessageSessionStatusCode.encryptionDecryptionCompleted;
            return encryptedMessageData;
        }

        internal string DecryptShortSingleMessage(ICryptoLibrary cryptoLibrary, byte[] encryptedMessageData)
        {
            if (Status != MessageSessionStatusCode.inProgress) throw new InvalidOperationException();

            var decryptedMessageData = new byte[encryptedMessageData.Length];
            cryptoLibrary.ProcessAesCbcBlocks(false, _aesKey, _iv, encryptedMessageData, decryptedMessageData);

            var messageText = MessageEncoderDecoder.DecodePlainTextMessageWithPadding_plainTextUtf8_256(decryptedMessageData);

            Status = MessageSessionStatusCode.encryptionDecryptionCompleted;
            return messageText;
        }


        internal byte[] EncryptIke1Data(ICryptoLibrary cryptoLibrary, Ike1Data ike1Data)
        {
            if (Status != MessageSessionStatusCode.inProgress) throw new InvalidOperationException();

            var decryptedMessageData = MessageEncoderDecoder.EncodeIke1DataWithPadding(cryptoLibrary, ike1Data);
            var encryptedMessageData = new byte[decryptedMessageData.Length];

            cryptoLibrary.ProcessAesCbcBlocks(true, _aesKey, _iv, decryptedMessageData, encryptedMessageData);

            Status = MessageSessionStatusCode.encryptionDecryptionCompleted;
            return encryptedMessageData;
        }
        internal Ike1Data DecryptIke1Data(ICryptoLibrary cryptoLibrary, byte[] encryptedMessageData)
        {
            if (Status != MessageSessionStatusCode.inProgress) throw new InvalidOperationException();

            var decryptedMessageData = new byte[encryptedMessageData.Length];
            cryptoLibrary.ProcessAesCbcBlocks(false, _aesKey, _iv, encryptedMessageData, decryptedMessageData);

            var r = MessageEncoderDecoder.DecodeIke1DataWithPadding(decryptedMessageData);
            
            Status = MessageSessionStatusCode.encryptionDecryptionCompleted;
            return r;
        }
    }


    enum MessageSessionStatusCode
    {
        created = 0,
        inProgress = 1,
        canceled = 2,
        encryptionDecryptionCompleted = 3,
        finalSignatureVerified = 4
    }
}
