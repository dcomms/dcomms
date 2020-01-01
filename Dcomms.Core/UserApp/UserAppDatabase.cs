using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Dcomms.Cryptography;
using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.Vision;
using SQLite;

namespace Dcomms.UserApp.DataModels
{
    public class UserAppDatabase: IDisposable
    {
        const string VisionChannelModuleName = "db";
        SQLiteConnection _db_main;
        SQLiteConnection _db_messages;

        readonly ICryptoLibrary _cryptoLibrary;
        readonly IDatabaseKeyProvider _keyProvider;
        readonly VisionChannel _visionChannel;
        readonly string _visionChannelSourceId;
        public UserAppDatabase(ICryptoLibrary cryptoLibrary, IDatabaseKeyProvider keyProvider, VisionChannel visionChannel, string visionChannelSourceId, string basePathNullable)
        {
            _keyProvider = keyProvider;
            _cryptoLibrary = cryptoLibrary;
            _visionChannel = visionChannel;
            _visionChannelSourceId = visionChannelSourceId;
            
            if (basePathNullable == null)
                basePathNullable = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var mainDatabaseFileName = Path.Combine(basePathNullable,  "dcomms_main.db");
            WriteToLog_deepDetail($"basePath={basePathNullable}, databaseFileName={mainDatabaseFileName}");

            _db_main = new SQLiteConnection(mainDatabaseFileName);
            _db_main.CreateTable<User>(CreateFlags.None);
            _db_main.CreateTable<RootUserKeys>(CreateFlags.None);
            _db_main.CreateTable<UserRegistrationID>(CreateFlags.None);
            WriteToLog_deepDetail($"initialized sqlite database");
        }
        public void Dispose()
        {
            _db_main?.Dispose();
            _db_main = null;
            WriteToLog_deepDetail($"disposed sqlite database");
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

        #region insert & query
        /// <summary>
        /// encrypts, signs, inserts data
        /// </summary>
        public void InsertUser(User user)
        {
            // get ID of the new record
            _db_main.Insert(user);

            EncryptAndSign(user.UserID.Encode(), user.Id, EncryptedFieldIds.User_UserID, out var e, out var a);
            user.UserID_encrypted = e; user.UserID_hmac = a;
            
            EncryptAndSign(BinaryProcedures.EncodeString2UTF8(user.AliasID), user.Id, EncryptedFieldIds.User_AliasID, out e, out a); 
            user.AliasID_encrypted = e; user.AliasID_hmac = a;
            
            EncryptAndSign(user.LocalUserCertificate?.Encode(true), user.Id, EncryptedFieldIds.User_LocalUserCertificate, out e, out a);
            user.LocalUserCertificate_encrypted = e; user.LocalUserCertificate_hmac = a;

            EncryptAndSign(user.Metadata?.Encode(), user.Id, EncryptedFieldIds.User_Metadata, out e, out a);
            user.Metadata_encrypted = e; user.Metadata_hmac = a;

            _db_main.Update(user);

            WriteToLog_deepDetail($"inserted user '{user.AliasID}'");
        }
        public List<User> GetUsers(bool local)
        {
            var query = _db_main.Table<User>();
            if (local) query = query.Where(x => x.OwnerLocalUserId == 0);
            else query = query.Where(x => x.OwnerLocalUserId != 0);
            var users = query.ToList();
            foreach (var u in users)
            {
                try
                {
                    u.UserID = UserId.Decode(DecryptAndVerify(u.UserID_encrypted, u.UserID_hmac, u.Id, EncryptedFieldIds.User_UserID));                                   
                    u.AliasID = BinaryProcedures.DecodeString2UTF8(DecryptAndVerify(u.AliasID_encrypted, u.AliasID_hmac, u.Id, EncryptedFieldIds.User_AliasID));
                    u.LocalUserCertificate = UserCertificate.Decode(DecryptAndVerify(u.LocalUserCertificate_encrypted, u.LocalUserCertificate_hmac, u.Id, EncryptedFieldIds.User_LocalUserCertificate));
                    u.Metadata = UserMetadata.Decode(DecryptAndVerify(u.Metadata_encrypted, u.Metadata_hmac, u.Id, EncryptedFieldIds.User_Metadata));
                    WriteToLog_deepDetail($"decrypted user '{u.AliasID}'");
                }
                catch (Exception exc)
                {
                    HandleException($"can not decrypt/verify user ID={u.UserID}: ", exc);
                }
            }
            return users;
          //  var user = db.Get<UserId>(5); // primary key id of 5
        }
        
        public void InsertRootUserKeys(RootUserKeys rootUserKeys)
        {
            // get ID of the new record
            _db_main.Insert(rootUserKeys);

            EncryptAndSign(rootUserKeys.UserRootPrivateKeys.Encode(), rootUserKeys.Id, EncryptedFieldIds.RootUserKeys_UserRootPrivateKeys, out var e, out var a);
            rootUserKeys.UserRootPrivateKeys_encrypted = e; rootUserKeys.UserRootPrivateKeys_hmac = a;

            _db_main.Update(rootUserKeys);

            WriteToLog_deepDetail($"inserted rootUserKeys '{rootUserKeys.UserId}'");
        }
        public RootUserKeys GetRootUserKeys(int userId)
        {
            var r = _db_main.Table<RootUserKeys>().FirstOrDefault(x => x.UserId == userId);
            if (r == null) return null;
            
            r.UserRootPrivateKeys = UserRootPrivateKeys.Decode(DecryptAndVerify(r.UserRootPrivateKeys_encrypted, r.UserRootPrivateKeys_hmac, r.Id, EncryptedFieldIds.RootUserKeys_UserRootPrivateKeys));
             //       WriteToLog_deepDetail($"decrypted RootUserKeys '{k.Id}'");               
            return r;
        }


        public void InsertUserRegistrationID(UserRegistrationID userRegistrationID)
        {
            // get ID of the new record
            _db_main.Insert(userRegistrationID);

            EncryptAndSign(userRegistrationID.RegistrationId.Encode(), userRegistrationID.Id, EncryptedFieldIds.UserRegistrationID_RegistrationId, out var e, out var a);
            userRegistrationID.RegistrationId_encrypted = e; userRegistrationID.RegistrationId_hmac = a;

            EncryptAndSign(userRegistrationID.RegistrationPrivateKey.Encode(), userRegistrationID.Id, EncryptedFieldIds.UserRegistrationID_RegistrationPrivateKey, out e, out a);
            userRegistrationID.RegistrationPrivateKey_encrypted = e; userRegistrationID.RegistrationPrivateKey_hmac = a;

            _db_main.Update(userRegistrationID);

            WriteToLog_deepDetail($"inserted userRegistrationID '{userRegistrationID.UserId}'");
        }
        public Dictionary<int,List<UserRegistrationID>> GetUserRegistrationIDs()
        {          
            var r = new Dictionary<int, List<UserRegistrationID>>();
            foreach (var regId in _db_main.Table<UserRegistrationID>())
            {
                regId.RegistrationId = RegistrationId.Decode(DecryptAndVerify(regId.RegistrationId_encrypted, regId.RegistrationId_hmac, regId.Id, EncryptedFieldIds.UserRegistrationID_RegistrationId));
                regId.RegistrationPrivateKey = RegistrationPrivateKey.Decode(DecryptAndVerify(regId.RegistrationPrivateKey_encrypted, regId.RegistrationPrivateKey_hmac, regId.Id, EncryptedFieldIds.UserRegistrationID_RegistrationPrivateKey));
                //  WriteToLog_deepDetail($"decrypted userRegistrationID '{r.Id}'");
                if (!r.TryGetValue(regId.UserId, out var list))
                {
                    list = new List<UserRegistrationID>();
                    r.Add(regId.UserId, list);
                }                
                list.Add(regId);
            }
            return r;
        }
        #endregion

        #region delete
        public void DeleteUser(int userId)
        {
            _db_main.Delete<User>(userId);
        }
        public void DeleteRegistrationId(int registrationId)
        {
            _db_main.Delete<UserRegistrationID>(registrationId);

        }
        public void DeleteRootUserKeys(int rootUserKeysId)
        {
            _db_main.Delete<RootUserKeys>(rootUserKeysId);

        }
        #endregion

        #region update
        public void UpdateUser(User user)
        {
            // we update only following fields:

            EncryptAndSign(BinaryProcedures.EncodeString2UTF8(user.AliasID), user.Id, EncryptedFieldIds.User_AliasID, out var e, out var a);
            user.AliasID_encrypted = e; user.AliasID_hmac = a;
                       
            _db_main.Update(user);
        }
        #endregion

        #region vision
        void HandleException(string prefix, Exception exc)
        {
            WriteToLog_mediumPain($"{prefix}{exc}");
        }

        internal bool WriteToLog_deepDetail_enabled => _visionChannel?.GetAttentionTo(_visionChannelSourceId, VisionChannelModuleName) <= AttentionLevel.deepDetail;
        void WriteToLog_deepDetail(string msg)
        {
            if (WriteToLog_deepDetail_enabled)
                _visionChannel?.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, msg);
        }
        void WriteToLog_mediumPain(string msg)
        {
            _visionChannel?.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.mediumPain, msg);
        }
        #endregion
    }


    public interface IDatabaseKeyProvider
    {
        byte[] HsmOperation(byte[] input); // 16 bytes
    }
       

    enum EncryptedFieldIds // 1 byte
    {
        User_UserID = 1,
        User_AliasID = 2,
        User_LocalUserCertificate = 3,

        RootUserKeys_UserRootPrivateKeys = 4,
        
        UserRegistrationID_RegistrationId = 5,
        UserRegistrationID_RegistrationPrivateKey = 6,

        User_Metadata = 7,
    }
}
