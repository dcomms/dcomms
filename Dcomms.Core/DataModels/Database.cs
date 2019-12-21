using System;
using System.Collections.Generic;
using System.IO;
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
        byte[] _hmacKey;
        byte[] _aesKey;
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
        internal void EncryptAndSign(byte[] inputNullable, int id, EncryptedFieldIds fieldId, out byte[] encrypted, out byte[] hmac)
        {
            throw new NotImplementedException();
        }
        internal byte[] DecryptAndVerify(byte[] encryptedNullable, byte[] hmacNullable, int id, EncryptedFieldIds fieldId)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// encrypts, signs, inserts data
        /// </summary>
        public void InsertUser(User user)
        {
            // get ID of the new record
            _db.Insert(user);

            EncryptAndSign(user.UserID.Encode(), user.Id, EncryptedFieldIds.User_UserID, out var a, out var e);
            user.UserID_encrypted = e; user.UserID_hmac = a;
            
            EncryptAndSign(BinaryProcedures.EncodeString2UTF8_padding32(user.AliasID, _cryptoLibrary.InsecureRandom), user.Id, EncryptedFieldIds.User_AliasID, out a, out e); 
            user.AliasID_encrypted = e; user.AliasID_hmac = a;


            EncryptAndSign(user.LocalUserCertificate?.Encode(), user.Id, EncryptedFieldIds.User_LocalUserCertificate, out a, out e);
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
                    u.AliasID = BinaryProcedures.DecodeString2UTF8_padding32(DecryptAndVerify(u.AliasID_encrypted, u.AliasID_hmac, u.Id, EncryptedFieldIds.User_AliasID));
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
    }
       

    enum EncryptedFieldIds // 1 byte
    {
        User_UserID = 1,
        User_AliasID = 2,
        User_LocalUserCertificate = 3,        
    }
}
