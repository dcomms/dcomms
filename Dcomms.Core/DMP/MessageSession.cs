using Dcomms.Cryptography;
using Dcomms.DMP.Packets;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dcomms.DMP
{
    class MessageSession
    {
        public uint MessageId32;

        public MessageSessionStatusCode Status { get; private set; } = MessageSessionStatusCode.created;

        byte[] _iv, _aesKey; // encryption
        byte[] _hmacKey; // authentication

        internal void DeriveKeys(ICryptoLibrary cryptoLibrary, byte[] sharedPingPongHmacKey, MessageStartPacket messageStart,
            byte[] directChannelSharedDhSecretA,
            byte[] directChannelSharedDhSecretE)
        {
            if (Status != MessageSessionStatusCode.created) throw new InvalidOperationException();

            PacketProcedures.CreateBinaryWriter(out var msE, out var wE);
            messageStart.GetSignedFieldsForMessageSessionHMAC(wE, false);
            wE.Write(sharedPingPongHmacKey);
            _iv = cryptoLibrary.GetHashSHA256(msE.ToArray()).Take(16).ToArray();
            wE.Write(directChannelSharedDhSecretE);
            _aesKey = cryptoLibrary.GetHashSHA256(msE.ToArray());
            
            PacketProcedures.CreateBinaryWriter(out var msA, out var wA);
            messageStart.GetSignedFieldsForMessageSessionHMAC(wA, false);
            wA.Write(sharedPingPongHmacKey);
            wA.Write(directChannelSharedDhSecretA);
            _hmacKey = cryptoLibrary.GetHashSHA256(msA.ToArray());

            Status = MessageSessionStatusCode.inProgress;
        }


        internal byte[] EncryptShortSingleMessage(ICryptoLibrary cryptoLibrary, string messageText)
        {
            if (Status != MessageSessionStatusCode.inProgress) throw new InvalidOperationException();

            var decryptedMessageData = MessageEncoderDecoder.EncodePlainTextMessageWithPadding_plainTextUtf8_256(cryptoLibrary, messageText);          
            var encryptedMessageData = new byte[decryptedMessageData.Length];
           
            cryptoLibrary.ProcessAesCbcBlocks(true, _aesKey, _iv, decryptedMessageData, encryptedMessageData);

            Status = MessageSessionStatusCode.finishedSuccessfully;
            return encryptedMessageData;
        }

        internal string DecryptShortSingleMessage(ICryptoLibrary cryptoLibrary, byte[] encryptedMessageData)
        {
            if (Status != MessageSessionStatusCode.inProgress) throw new InvalidOperationException();





            Status = MessageSessionStatusCode.finishedSuccessfully;
            return messageText;
        }

    }


    enum MessageSessionStatusCode
    {
        created = 0,
        inProgress = 1,
        canceled = 2,
        finishedSuccessfully = 3,
    }
}
