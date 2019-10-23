using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.Sandbox
{
    class DrpTesterPeerApp : IDrpRegisteredPeerApp
    {
        const string VisionChannelModuleName = "drpTesterApp";
        public readonly UserRootPrivateKeys UserRootPrivateKeys;
        public readonly UserId UserId;

        public override string ToString() => DrpPeerEngine.Configuration.VisionChannelSourceId;
        public readonly UserCertificate UserCertificateWithPrivateKey;
        public readonly DrpPeerEngine DrpPeerEngine;
        public readonly LocalDrpPeerConfiguration DrpPeerRegistrationConfiguration;
        public LocalDrpPeer LocalDrpPeer;
        public DrpTesterPeerApp(DrpPeerEngine drpPeerEngine, LocalDrpPeerConfiguration drpPeerRegistrationConfiguration)
        {
            DrpPeerRegistrationConfiguration = drpPeerRegistrationConfiguration;
            DrpPeerEngine = drpPeerEngine;
            UserRootPrivateKeys.CreateUserId(3, 2, DrpPeerEngine.CryptoLibrary, out UserRootPrivateKeys, out UserId);
            UserCertificateWithPrivateKey = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(DrpPeerEngine.CryptoLibrary, UserId, UserRootPrivateKeys, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        }

        public void OnReceivedShortSingleMessage(string message)
        {
            DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.guiActivity,
                $"received message: {message}");
        }
        public readonly Dictionary<RegistrationId, UserId> ContactBookUsersByRegId = new Dictionary<RegistrationId, UserId>();
        public void OnReceivedInvite(RegistrationId remoteRegistrationId, out DMP.UserId remoteUserId, out DMP.UserCertificate localUserCertificateWithPrivateKey, out bool autoReceiveShortSingleMessage)
        {
            remoteUserId = ContactBookUsersByRegId[remoteRegistrationId];
            localUserCertificateWithPrivateKey = UserCertificateWithPrivateKey;
            autoReceiveShortSingleMessage = true;
        }
    }
}
