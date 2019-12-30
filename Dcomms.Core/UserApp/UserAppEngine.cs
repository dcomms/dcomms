using Dcomms.UserApp.DataModels;
using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    public class UserAppEngine :IDisposable, IVisibleModule
    {
        const string VisionChannelSourceId = "UA";
        internal const string VisionChannelModuleName = "UA";
        DrpPeerEngine _drpPeerEngine;
        public DrpPeerEngine Engine => _drpPeerEngine;
        UserAppDatabase _db;
        public string Status => $"todo";
        internal readonly UserAppConfiguration Configuration;
        public UserAppEngine(UserAppConfiguration configuration, VisionChannel visionChannel)
        {
            Configuration = configuration;
            _visionChannel = visionChannel;
            _visionChannel.ClearModules();
         
            _visionChannel.RegisterVisibleModule(VisionChannelSourceId, "UA", this);

            _drpPeerEngine = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                VisionChannel = _visionChannel,
                VisionChannelSourceId = VisionChannelSourceId  
            });

            _db = new UserAppDatabase(_drpPeerEngine.CryptoLibrary, configuration.DatabaseKeyProvider, _visionChannel, VisionChannelSourceId, configuration.DatabaseBasePathNullable);
            
            LocalUsers = new Dictionary<int, LocalUser>();
            var userRegistrationIDs = _db.GetUserRegistrationIDs();
            foreach (var u in _db.GetUsers(true))
            {
                var rootUserKeys = _db.GetRootUserKeys(u.Id);
                if (rootUserKeys != null)
                {
                    var localUser = new LocalUser
                    {
                        User = u,
                        RootUserKeys = rootUserKeys,
                    };
                    if (!userRegistrationIDs.TryGetValue(u.Id, out localUser.UserRegistrationIDs))
                        localUser.UserRegistrationIDs = new List<UserRegistrationID>();
                    LocalUsers.Add(u.Id, localUser);
                    localUser.CreateLocalDrpPeers(this);
                }
            }
            WriteToLog_deepDetail($"loaded {LocalUsers.Count} local users");


            foreach (var contactUser in _db.GetUsers(false))
                if (LocalUsers.TryGetValue(contactUser.OwnerLocalUserId, out var localUser))
                {
                    var contact = new Contact
                    {
                        User = contactUser
                    };
                    if (!userRegistrationIDs.TryGetValue(contact.User.Id, out contact.RegistrationIDs))
                        contact.RegistrationIDs = new List<UserRegistrationID>();
                    localUser.Contacts.Add(contact.User.Id, contact);
                }
        }
        public void Dispose()
        {
            WriteToLog_deepDetail(">> UserAppEngine.Dispose()");
            WriteToLog_deepDetail($"destroying local users...");
            foreach (var localUser in LocalUsers.Values)
                localUser.Dispose();

            WriteToLog_deepDetail($"destroying DRP peer engine...");
            _drpPeerEngine.Dispose();

            WriteToLog_deepDetail($"destroying database...");
            _db?.Dispose();
            _db = null;
            WriteToLog_deepDetail("<< UserAppEngine.Dispose()");
        }

        #region database entities, operations
        public Dictionary<int,LocalUser> LocalUsers;
        public void DeleteLocalUser(LocalUser localUser)
        {
            try
            {
                foreach (var regId in localUser.UserRegistrationIDs)
                    _db.DeleteRegistrationId(regId.Id);
                if (localUser.RootUserKeys != null) _db.DeleteRootUserKeys(localUser.RootUserKeys.Id);
                _db.DeleteUser(localUser.User.Id);

                LocalUsers.Remove(localUser.User.Id);
                localUser.Dispose();
            }
            catch (Exception exc)
            {
                HandleException("error when deleting local user: ", exc);
            }
        }
        public void AddLocalUser(string aliasId)
        {
            try
            {
                UserRootPrivateKeys.CreateUserId(1, 1, TimeSpan.FromDays(3670), _drpPeerEngine.CryptoLibrary, out var userRootPrivateKeys, out var userId);
                var userCertificateWithPrivateKey = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_drpPeerEngine.CryptoLibrary, userId, userRootPrivateKeys, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddYears(9));

                var u = new User
                {
                    AliasID = aliasId,
                    UserID = userId,
                    OwnerLocalUserId = 0,
                    LocalUserCertificate = userCertificateWithPrivateKey,
                };
                _db.InsertUser(u);

                var ruk = new RootUserKeys
                {
                    UserId = u.Id,
                    UserRootPrivateKeys = userRootPrivateKeys
                };
                _db.InsertRootUserKeys(ruk);

                RegistrationId.CreateNew(_drpPeerEngine.CryptoLibrary, out var regPrivateKey, out var registrationId);

                var regId = new UserRegistrationID
                {
                    UserId = u.Id,
                    RegistrationId = registrationId,
                    RegistrationPrivateKey = regPrivateKey
                };
                _db.InsertUserRegistrationID(regId);

                var newLocalUser = new LocalUser
                {
                    User = u,
                    RootUserKeys = ruk,
                    UserRegistrationIDs = new List<UserRegistrationID> { regId }
                };
                LocalUsers.Add(u.Id, newLocalUser);
                newLocalUser.CreateLocalDrpPeers(this);
            }
            catch (Exception exc)
            {
                HandleException("error when adding new local user: ", exc);
            }
        }
        public void UpdateLocalUser(LocalUser user, LocalUser newFieldsUser)
        {
            try
            {
                user.User.AliasID = newFieldsUser.UserAliasID;
                _db.UpdateUser(user.User);
            }
            catch (Exception exc)
            {
                HandleException("error when updating local user: ", exc);
            }
        }
        /// <summary>
        /// inserts User record and RegistrationIds
        /// </summary>
        public void ConfirmContact(Contact contact)
        {
            _db.InsertUser(contact.User);
            foreach (var regId in contact.RegistrationIDs)
            {
                regId.UserId = contact.User.Id;
                _db.InsertUserRegistrationID(regId);
            }
        }
        #endregion

        #region vision
        readonly VisionChannel _visionChannel;
        internal VisionChannel VisionChannel => _visionChannel;
        void HandleException(string prefix, Exception exc)
        {
            WriteToLog_mediumPain($"{prefix}{exc}");
        }

        internal bool WriteToLog_deepDetail_enabled => _visionChannel?.GetAttentionTo(VisionChannelSourceId, VisionChannelModuleName) <= AttentionLevel.deepDetail;
        void WriteToLog_deepDetail(string msg)
        {
            if (WriteToLog_deepDetail_enabled)
                _visionChannel?.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, msg);
        }
        void WriteToLog_mediumPain(string msg)
        {
            _visionChannel?.Emit(VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.mediumPain, msg);
        }
        #endregion
    }

    class EmptyDatabaseKeyProvider : IDatabaseKeyProvider
    {
        public byte[] HsmOperation(byte[] input)
        {
            return input;
        }
    }
}
