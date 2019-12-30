using Dcomms.DMP;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
using Dcomms.UserApp.DataModels;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        #endregion

        #region contacts
        public readonly Dictionary<int, Contact> Contacts = new Dictionary<int, Contact>();
        int _unconfirmedContactIdCounter = -1;
        public string NewContactAliasID { get; set; }
        public string NewContact_RemotelyInitiatedInvitationKey { get; set; }
        public string NewContact_LocallyInitiatedInvitationKey_NewValueEveryTime => ContactInvitation.CreateNew(_userAppEngine.Engine.CryptoLibrary, UserRegistrationIDs.First().RegistrationId).EncodeForUI();        
        public string NewContact_LocallyInitiatedInvitationKey { get; set; } // is set by UI
        public void AddNewContact_LocallyInitiated(string aliasID, string locallyInitiatedInvitationKey)
        {
            var unconfirmedContactId = _unconfirmedContactIdCounter--;
            var locallyInitiatedInvitation = ContactInvitation.DecodeFromUI(locallyInitiatedInvitationKey);
            Contacts.Add(unconfirmedContactId, new Contact()
            {
                UnconfirmedContactId = unconfirmedContactId,
                UnconfirmedContactOwnerLocalUserId = User.Id, 
                LocallyGeneratedInvitation = locallyInitiatedInvitation,
                UserAliasID = aliasID
            });
        }

        Contact GetUnconfirmedContactByToken(byte[] contactInvitationToken)
        {
            return Contacts.Values.FirstOrDefault(x => MiscProcedures.EqualByteArrays(x.LocallyGeneratedInvitation.ContactInvitationToken, contactInvitationToken) == true);
        }
        (UserId, RegistrationId[]) IDrpRegisteredPeerApp.OnReceivedInvite_ContactInvitation_GetLocal(byte[] contactInvitationToken)
        {
            var unconfirmedContact = GetUnconfirmedContactByToken(contactInvitationToken);
            if (unconfirmedContact == null) return (null, null);
            return (User.UserID, UserRegistrationIDs.Select(x => x.RegistrationId).ToArray());
        }
        void IDrpRegisteredPeerApp.OnReceivedInvite_ContactInvitation_SetRemote(byte[] contactInvitationToken, (UserId, RegistrationId[], IPEndPoint) remoteContactInvitation)
        {
            var contact = GetUnconfirmedContactByToken(contactInvitationToken);
            if (contact == null) throw new BadSignatureException();

            contact.User = new User
            {
                OwnerLocalUserId = this.User.Id,
                AliasID = contact.UserAliasID, 
                UserID = remoteContactInvitation.Item1,
                Metadata = new UserMetadata { ContactCreatedAtUTC = _userAppEngine.Engine.DateTimeNowUtc, ContactCreatedWithRemoteEndpoint = remoteContactInvitation.Item3 }
            };
            contact.RegistrationIDs = remoteContactInvitation.Item2.Select(x => new UserRegistrationID { RegistrationId = x, UserId = contact.User.Id }).ToList();
            _userAppEngine.ConfirmContact(contact);

            Contacts.Remove(contact.UnconfirmedContactId.Value);
            Contacts.Add(contact.User.Id, contact);
        }
        public void AddNewContact_RemotelyInitiated(string aliasID, string remotelyInitiatedInvitationKey)
        {
            var localDrpPeer = UserRegistrationIDs.Select(x => x.LocalDrpPeer).FirstOrDefault();
            if (localDrpPeer == null) throw new InvalidOperationException("no local DRP peers found to send INVITE");

            var unconfirmedContactId = _unconfirmedContactIdCounter--;
            var remotelyInitiatedInvitation = ContactInvitation.DecodeFromUI(remotelyInitiatedInvitationKey);
            var newContact = new Contact()
            {
                UnconfirmedContactId = unconfirmedContactId,
                UnconfirmedContactOwnerLocalUserId = User.Id,
                RemotelyGeneratedInvitation = remotelyInitiatedInvitation,
                UserAliasID = aliasID
            };
            Contacts.Add(unconfirmedContactId, newContact);

            // send invite with remote inv. key  (token + reg ID)
            localDrpPeer.BeginSendContactInvitation(User.LocalUserCertificate, User.UserID, 
                UserRegistrationIDs.Select(x => x.RegistrationId).ToArray(), 
                remotelyInitiatedInvitation,
                TimeSpan.FromMinutes(10),
                (exc, remoteUserId, remoteRegistrationIds, remoteEndpoint) =>
                {
                    newContact.User = new User 
                    {
                        OwnerLocalUserId = this.User.Id,
                        AliasID = newContact.UserAliasID, 
                        UserID = remoteUserId,
                        Metadata = new UserMetadata { ContactCreatedAtUTC = _userAppEngine.Engine.DateTimeNowUtc, ContactCreatedWithRemoteEndpoint = remoteEndpoint }
                    };
                    newContact.RegistrationIDs = remoteRegistrationIds.Select(x => new UserRegistrationID { RegistrationId = x, UserId = newContact.User.Id }).ToList();
                    
                    Contacts.Remove(newContact.UnconfirmedContactId.Value);
                    Contacts.Add(newContact.User.Id, newContact);
                });
        }


        public void DeleteContact(Contact contact)
        {
            if (contact.IsConfirmed)
                throw new NotImplementedException();
            else
            {
                Contacts.Remove(contact.ContactId);
            }
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
        #endregion




        void IDrpRegisteredPeerApp.OnReceivedShortSingleMessage(string messageText, InviteRequestPacket req)
        {
            throw new NotImplementedException();
        }
        void IDrpRegisteredPeerApp.OnReceivedInvite(RegistrationId remoteRegistrationId, out UserId remoteUserId, out UserCertificate localUserCertificateWithPrivateKey, out bool autoReceiveShortSingleMessage)
        {
            throw new NotImplementedException();
        }

    }
}
