using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dcomms.Cryptography;
using Dcomms.DMP;
using SQLite;

namespace Dcomms.DataModels
{
    public interface IDatabaseKeyProvider
    {
        byte[] HsmOperation(byte[] input); // 16 bytes
    }
    public class Database: IDisposable
    {
        SQLiteConnection _db;
    
        readonly ICryptoLibrary _cryptoLibrary;
        readonly IDatabaseKeyProvider _keyProvider;
        public Database(ICryptoLibrary cryptoLibrary, IDatabaseKeyProvider keyProvider)
        {
            _keyProvider = keyProvider;
            _cryptoLibrary = cryptoLibrary;
            string dbPath = Path.Combine(
                 Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                 "dcomms.db3");

            _db = new SQLiteConnection(dbPath);               
            _db.CreateTable<User>(CreateFlags.None);  
        }
        public void Dispose()
        {
            _db?.Dispose();
            _db = null;
        }
        void GetIVandKeys(int id, EncryptedFieldIds fieldId, out byte[] iv, out byte[] aesKey, out byte[] hmacKey)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write(id);
            w.Write((byte)fieldId);

            var sha256result = _cryptoLibrary.GetHashSHA256(ms.ToArray());
            var preKey = _keyProvider.HsmOperation(sha256result);
            _cryptoLibrary.DeriveKeysRFC5869_32bytes(preKey, sha256result, out aesKey, out hmacKey);
            iv = sha256result.Take(16).ToArray();

        }
        internal void EncryptAndSign(byte[] inputNullable, int id, EncryptedFieldIds fieldId, out byte[] encrypted, out byte[] hmac)
        {
            if (inputNullable == null)
            {
                encrypted = null;
                hmac = null;
                return;
            }
            if (inputNullable.Length > 65535) throw new ArgumentException(nameof(inputNullable));

            GetIVandKeys(id, fieldId, out var iv, out var aesKey, out var hmacKey);

            // encode length of input and add padding, to make it good for AES
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((ushort)inputNullable.Length);
            w.Write(inputNullable);
            var bytesInLastBlock = (int)ms.Position % CryptoLibraries.AesBlockSize;
            if (bytesInLastBlock != 0)
            {
                var bytesRemainingTillFullAesBlock = CryptoLibraries.AesBlockSize - bytesInLastBlock;
                w.Write(_cryptoLibrary.GetRandomBytes(bytesRemainingTillFullAesBlock));
            }

            var inputWithPadding = ms.ToArray();
            
            encrypted = new byte[inputWithPadding.Length];
            _cryptoLibrary.ProcessAesCbcBlocks(true, aesKey, iv, inputWithPadding, encrypted);

            hmac = _cryptoLibrary.GetSha256HMAC(hmacKey, encrypted);
        }
        internal byte[] DecryptAndVerify(byte[] encryptedNullable, byte[] hmacNullable, int id, EncryptedFieldIds fieldId)
        {
            if (encryptedNullable == null || hmacNullable == null)
                return null;

            GetIVandKeys(id, fieldId, out var iv, out var aesKey, out var hmacKey);

            var hmac = _cryptoLibrary.GetSha256HMAC(hmacKey, encryptedNullable);
            if (!MiscProcedures.EqualByteArrays(hmacNullable, hmac))
                throw new BadSignatureException();
             
            var decrypted = new byte[encryptedNullable.Length];
            _cryptoLibrary.ProcessAesCbcBlocks(false, aesKey, iv, encryptedNullable, decrypted);

            using var reader = BinaryProcedures.CreateBinaryReader(decrypted, 0);
            var dataLength = reader.ReadUInt16();
            return reader.ReadBytes(dataLength);
        }
        /// <summary>
        /// encrypts, signs, inserts data
        /// </summary>
        public void InsertUser(User user)
        {
            // get ID of the new record
            _db.Insert(user);

            EncryptAndSign(user.UserID.Encode(), user.Id, EncryptedFieldIds.User_UserID, out var e, out var a);
            user.UserID_encrypted = e; user.UserID_hmac = a;
            
            EncryptAndSign(BinaryProcedures.EncodeString2UTF8(user.AliasID), user.Id, EncryptedFieldIds.User_AliasID, out e, out a); 
            user.AliasID_encrypted = e; user.AliasID_hmac = a;


            EncryptAndSign(user.LocalUserCertificate?.Encode(), user.Id, EncryptedFieldIds.User_LocalUserCertificate, out e, out a);
            user.LocalUserCertificate_encrypted = e; user.LocalUserCertificate_hmac = a;
                                 
            _db.Update(user);
        }

        public List<User> GetUsers()
        {      
            var users = _db.Table<User>().ToList();
            foreach (var u in users)
            {
                try
                {
                    u.UserID = UserId.Decode(DecryptAndVerify(u.UserID_encrypted, u.UserID_hmac, u.Id, EncryptedFieldIds.User_UserID));                                   
                    u.AliasID = BinaryProcedures.DecodeString2UTF8(DecryptAndVerify(u.AliasID_encrypted, u.AliasID_hmac, u.Id, EncryptedFieldIds.User_AliasID));
                    u.LocalUserCertificate = UserCertificate.Decode(DecryptAndVerify(u.LocalUserCertificate_encrypted, u.LocalUserCertificate_hmac, u.Id, EncryptedFieldIds.User_LocalUserCertificate), u.UserID);
                }
                catch (Exception exc)
                {
                    HandleDecryptionVerificationException(exc);
                }
            }
            return users;
          //  var user = db.Get<UserId>(5); // primary key id of 5
        }
        void HandleDecryptionVerificationException(Exception exc)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// both local user IDs and remote user IDs (=contact book)
    /// </summary>
    [Table("Users")]
    public class User
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Ignore]
        public UserId UserID { get; set; }
        public byte[] UserID_encrypted { get; set; }      
        public byte[] UserID_hmac { get; set; }

        [Ignore]
        public string AliasID { get; set; }
        public byte[] AliasID_encrypted { get; set; }
        public byte[] AliasID_hmac { get; set; }
        
        [Ignore]
        public bool IsLocal => LocalUserCertificate_encrypted != null;
        [Ignore]
        public UserCertificate LocalUserCertificate { get; set; }      
        public byte[] LocalUserCertificate_encrypted { get; set; }
        public byte[] LocalUserCertificate_hmac { get; set; }

        public override string ToString() => AliasID;
    }
       

    enum EncryptedFieldIds // 1 byte
    {
        User_UserID = 1,
        User_AliasID = 2,
        User_LocalUserCertificate = 3,        
    }
}
