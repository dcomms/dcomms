using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
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
        public bool EchoMessages;
        public DrpTesterPeerApp(DrpPeerEngine drpPeerEngine, LocalDrpPeerConfiguration drpPeerRegistrationConfiguration, UserRootPrivateKeys userRootPrivateKeys = null, UserId userId = null)
        {
            DrpPeerRegistrationConfiguration = drpPeerRegistrationConfiguration;
            DrpPeerEngine = drpPeerEngine;
            if (userRootPrivateKeys == null || userId == null)
                UserRootPrivateKeys.CreateUserId(3, 2, TimeSpan.FromDays(366), DrpPeerEngine.CryptoLibrary, out UserRootPrivateKeys, out UserId);
            else
            {
                UserId = userId;
                UserRootPrivateKeys = userRootPrivateKeys;
            }
            UserCertificateWithPrivateKey = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(DrpPeerEngine.CryptoLibrary, UserId, UserRootPrivateKeys, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddYears(1));
        }
        public string LatestReceivedTextMessage { get; private set; }
        public InviteRequestPacket LatestReceivedTextMessage_req;
        public void OnReceivedShortSingleMessage(string message, InviteRequestPacket req)
        {
            DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.guiActivity,
                $"received message: {message}");
            LatestReceivedTextMessage_req = req;
            LatestReceivedTextMessage = message;

            if (EchoMessages)
            {
                DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.guiActivity,
                    $"echoing message: {message}");
                DrpPeerEngine.EngineThreadQueue.EnqueueDelayed(TimeSpan.FromMilliseconds(20), () =>
                {
                    var userCertificate1 = UserCertificate.GenerateKeyPairsAndSignAtSingleDevice(DrpPeerEngine.CryptoLibrary, UserId,
                                UserRootPrivateKeys, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

                    LocalDrpPeer.BeginSendShortSingleMessage(userCertificate1, req.RequesterRegistrationId,
                        ContactBookUsersByRegId[req.RequesterRegistrationId], message, TimeSpan.FromSeconds(60),
                        (exc) =>
                        {
                            if (exc == null)
                                DrpPeerEngine.Configuration.VisionChannel.Emit(DrpPeerEngine.Configuration.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.guiActivity,
                                    $"successfully echoed message: {message}");
                            else
                                DrpPeerEngine.Configuration.VisionChannel.EmitListOfPeers(DrpPeerEngine.Configuration.VisionChannelSourceId, VisionChannelModuleName, AttentionLevel.strongPain,
                                    $"could not send echoed message: {message}: {exc}");

                        });
                }, "echo 5096");                
            }
        }
        public Dictionary<RegistrationId, UserId> ContactBookUsersByRegId = new Dictionary<RegistrationId, UserId>();
        public void OnReceivedInvite(RegistrationId remoteRegistrationId, out DMP.UserId remoteUserId, out DMP.UserCertificate localUserCertificateWithPrivateKey, out bool autoReceiveShortSingleMessage)
        {
            remoteUserId = ContactBookUsersByRegId[remoteRegistrationId];
            localUserCertificateWithPrivateKey = UserCertificateWithPrivateKey;
            autoReceiveShortSingleMessage = true;
        }
    }
}
