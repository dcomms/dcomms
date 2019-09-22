using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DMP
{
    static class MessageEncoderDecoder
    {
        internal static byte[] EncodePlainTextMessageWithPadding_plainTextUtf8_256(ICryptoLibrary cryptoLibrary, string messageText)
        {
            var messageTextData = Encoding.UTF8.GetBytes(messageText);
            if (messageTextData.Length > 255) throw new ArgumentException();
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)EncodedDataType.plainTextUtf8_256);
            w.Write((byte)messageTextData.Length);
            w.Write(messageTextData);
            
            // add padding with random data
            var bytesInLastBlock = (int)ms.Position % CryptoLibraries.AesBlockSize;
            if (bytesInLastBlock != 0)
            {
                var bytesRemainingTillFullAesBlock = CryptoLibraries.AesBlockSize - bytesInLastBlock;
                w.Write(cryptoLibrary.GetRandomBytes(bytesRemainingTillFullAesBlock));
            }
                       
            return ms.ToArray();
        }
        internal static string DecodePlainTextMessage(byte[] decryptedMessageData)
        {
            var type = (EncodedDataType)decryptedMessageData[0];
            if (type != EncodedDataType.plainTextUtf8_256) throw new ArgumentException();
            var length = decryptedMessageData[1];
            return Encoding.UTF8.GetString(decryptedMessageData, 2, length);
        }
    }



    enum EncodedDataType
    {
        plainTextUtf8_256 = 0,
        plainTextUtf8_65536 = 1,
        htmlUtf8 = 2
            // gzip plaintext....
            // gzip html
            // genericFile
            // imageJpg
            // imagePng
    }
}
