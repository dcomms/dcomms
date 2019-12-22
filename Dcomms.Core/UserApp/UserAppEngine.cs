using Dcomms.DataModels;
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
        DrpPeerEngine _drpPeerEngine;
        Database _db;
        readonly VisionChannel _visionChannel;
        public string Status => $"todo";

        public UserAppEngine(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
            _visionChannel.ClearModules();
         
            _visionChannel.RegisterVisibleModule(VisionChannelSourceId, "UA", this);

            _drpPeerEngine = new DrpPeerEngine(new DrpPeerEngineConfiguration
            {
                VisionChannel = _visionChannel,
                VisionChannelSourceId = VisionChannelSourceId              
            });

            _db = new Database(_drpPeerEngine.CryptoLibrary, new EmptyDatabaseKeyProvider(), _visionChannel, VisionChannelSourceId);

            Test();
        }
        void Test()
        {

            UserRootPrivateKeys.CreateUserId(3, 2, TimeSpan.FromDays(367), _drpPeerEngine.CryptoLibrary, out var userRootPrivateKeys, out var userId);           
            var userCertificateWithPrivateKey = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(_drpPeerEngine.CryptoLibrary, userId, userRootPrivateKeys, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddYears(1));
                       
            _db.InsertUser(new User
            {
                AliasID = $"test{DateTime.Now}",
                UserID = userId,
                LocalUserCertificate = userCertificateWithPrivateKey,
            });


            _db.GetUsers();

        }
        public void Dispose()
        {
            _db?.Dispose();
            _db = null;
        }
    }

    class EmptyDatabaseKeyProvider : IDatabaseKeyProvider
    {
        public byte[] HsmOperation(byte[] input)
        {
            return input;
        }
    }
}
