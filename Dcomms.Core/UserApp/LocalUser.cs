using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
using Dcomms.UserApp.DataModels;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    /// <summary>
    /// = "my account" in GUI
    /// </summary>
    public class LocalUser : IDisposable, IDrpRegisteredPeerApp
    {
        #region database fields
        string _userAliasID;
        public string UserAliasID // used by web UI only
        {
            get
            {
                return _userAliasID ?? User?.AliasID;
            }
            set
            {
                _userAliasID = value;
            }
        }
        public User User;
        public RootUserKeys RootUserKeys;
        public List<UserRegistrationID> UserRegistrationIDs;

        public readonly Dictionary<int, Contact> Contacts = new Dictionary<int, Contact>();
        int _unconfirmedContactIdCounter = -1;
        public string NewContactAliasID { get; set; }
        public string NewContact_RemotelyInitiatedInvitationKey { get; set; }
        public string NewContact_LocallyInitiatedInvitationKey_NewValueEveryTime => "todo new invitation " + new Random().Next();
        public string NewContact_LocallyInitiatedInvitationKey { get; set; } // is set by UI
        public void AddNewContact_LocallyInitiated(string aliasID, string locallyInitiatedInvitationKey)
        {
            var unconfirmedContactId = _unconfirmedContactIdCounter--;
            Contacts.Add(unconfirmedContactId, new Contact() { UnconfirmedContactId = unconfirmedContactId, UnconfirmedContactOwnerLocalUserId = User.Id, LocallyGeneratedInvitationKey = locallyInitiatedInvitationKey, UserAliasID = aliasID });
        }
        public void AddNewContact_RemotelyInitiated(string aliasID, string remotelyInitiatedInvitationKey)
        {
            var unconfirmedContactId = _unconfirmedContactIdCounter--;
            Contacts.Add(unconfirmedContactId, new Contact() { UnconfirmedContactId = unconfirmedContactId, UnconfirmedContactOwnerLocalUserId = User.Id, RemotelyGeneratedInvitationKey = remotelyInitiatedInvitationKey, UserAliasID = aliasID });
        }
        #endregion

        UserAppEngine _userAppEngine;
        
        /// <summary>
        /// is executed every time when user is loaded from database, or when a new user is created
        /// </summary>
        public void CreateLocalDrpPeers(UserAppEngine userAppEngine)
        {
            _userAppEngine = userAppEngine;

            foreach (var regId in UserRegistrationIDs)
            {
                try
                {
                    var localDrpPeerConfiguration = LocalDrpPeerConfiguration.Create(_userAppEngine.Engine.CryptoLibrary, null,
                        regId.RegistrationPrivateKey.ed25519privateKey, regId.RegistrationId);
                    localDrpPeerConfiguration.EntryPeerEndpoints = _userAppEngine.Configuration.EpEndPoints;

                    _userAppEngine.Engine.BeginRegister(localDrpPeerConfiguration,
                        this,
                        (localDrpPeer) =>
                        {
                            regId.LocalDrpPeer = localDrpPeer;
                        }
                        );
                }
                catch (Exception exc)
                {
                    HandleException("error when registering local DRP peer: ", exc);
                }
            }
        }

        /// <summary>
        /// is executed when UserAppEngine shuts down, or when this acount is deleted in GUI
        /// </summary>
        public void Dispose()
        {
            foreach (var regId in UserRegistrationIDs)
                try
                {
                    regId.LocalDrpPeer?.Dispose();
                    regId.LocalDrpPeer = null;
                }
                catch (Exception exc)
                {
                    HandleException("error when destroying local DRP peer: ", exc);
                }
        }


        #region vision
        VisionChannel VisionChannel => _userAppEngine.VisionChannel;
        string VisionChannelSourceId => UserAliasID;
        void HandleException(string prefix, Exception exc)
        {
            WriteToLog_mediumPain($"{prefix}{exc}");
        }

        internal bool WriteToLog_deepDetail_enabled => VisionChannel?.GetAttentionTo(VisionChannelSourceId, UserAppEngine.VisionChannelModuleName) <= AttentionLevel.deepDetail;
        void WriteToLog_deepDetail(string msg)
        {
            if (WriteToLog_deepDetail_enabled)
                VisionChannel?.Emit(VisionChannelSourceId, UserAppEngine.VisionChannelModuleName, AttentionLevel.deepDetail, msg);
        }
        void WriteToLog_mediumPain(string msg)
        {
            VisionChannel?.Emit(VisionChannelSourceId, UserAppEngine.VisionChannelModuleName, AttentionLevel.mediumPain, msg);
        }

        void IDrpRegisteredPeerApp.OnReceivedShortSingleMessage(string messageText, InviteRequestPacket req)
        {
            throw new NotImplementedException();
        }

        void IDrpRegisteredPeerApp.OnReceivedInvite(RegistrationId remoteRegistrationId, out UserId remoteUserId, out UserCertificate localUserCertificateWithPrivateKey, out bool autoReceiveShortSingleMessage)
        {
            throw new NotImplementedException();
        }
        #endregion

    }

}
