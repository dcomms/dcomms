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
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
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
        internal static string DecodePlainTextMessageWithPadding_plainTextUtf8_256(byte[] decryptedMessageData)
        {
            var type = (EncodedDataType)decryptedMessageData[0];
            if (type != EncodedDataType.plainTextUtf8_256) throw new ArgumentException();
            var length = decryptedMessageData[1];
            return Encoding.UTF8.GetString(decryptedMessageData, 2, length);
        }


        internal static byte[] EncodeContactInvitationWithPadding(ICryptoLibrary cryptoLibrary, UserId userId, DRP.RegistrationId[] registrationIds)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            byte flags = 0;
            w.Write(flags);
            userId.Encode(w);
            w.Write((byte)registrationIds.Length);
            foreach (var localRegistrationId in registrationIds)
                localRegistrationId.Encode(w);

            // add padding with random data
            var bytesInLastBlock = (int)ms.Position % CryptoLibraries.AesBlockSize;
            if (bytesInLastBlock != 0)
            {
                var bytesRemainingTillFullAesBlock = CryptoLibraries.AesBlockSize - bytesInLastBlock;
                w.Write(cryptoLibrary.GetRandomBytes(bytesRemainingTillFullAesBlock));
            }

            return ms.ToArray();
        }
        internal static (UserId,DRP.RegistrationId[]) DecodeContactInvitationWithPadding(byte[] decryptedMessageData)
        {
            const byte flags_mustBeZero = 0b11100000;
            var reader = BinaryProcedures.CreateBinaryReader(decryptedMessageData, 0);
            var flags = reader.ReadByte();
            if ((flags & flags_mustBeZero) != 0) throw new NotImplementedException();

            var userId = UserId.Decode(reader);
            var regIds = new List<DRP.RegistrationId>();
            var regIdsCount = reader.ReadByte();
            for (int i = 0; i < regIdsCount; i++)
                regIds.Add(DRP.RegistrationId.Decode(reader));

            return (userId, regIds.ToArray());
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
