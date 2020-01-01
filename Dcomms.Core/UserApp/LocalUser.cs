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
        public string NewContact_RemotelyInitiatedIke1Invitation { get; set; }
        public string NewContact_LocallyInitiatedIke1Invitation_NewRandomValue => Ike1Invitation.CreateNew(_userAppEngine.Engine.CryptoLibrary, UserRegistrationIDs.First().RegistrationId).EncodeForUI();        
        public string NewContact_LocallyInitiatedIke1Invitation { get; set; } // is set by UI
        public void AddNewContact_LocallyInitiatedInvitation(string aliasID, string locallyInitiatedIke1Invitation)
        {
            var unconfirmedContactId = _unconfirmedContactIdCounter--;
            var locallyInitiatedInvitation = Ike1Invitation.DecodeFromUI(locallyInitiatedIke1Invitation);
            Contacts.Add(unconfirmedContactId, new Contact()
            {
                UnconfirmedContactId = unconfirmedContactId,
                UnconfirmedContactOwnerLocalUserId = User.Id, 
                LocallyInitiatedIke1Invitation = locallyInitiatedInvitation,
                UserAliasID = aliasID
            });
            // next we wait for INVITE with specific ContactInvitationToken
        }

        Contact GetUnconfirmedContactByToken(byte[] contactInvitationToken)
        {
            return Contacts.Values.FirstOrDefault(x => MiscProcedures.EqualByteArrays(x.LocallyInitiatedIke1Invitation.ContactInvitationToken, contactInvitationToken) == true);
        }

        void IDrpRegisteredPeerApp.OnReceivedInvite(RegistrationId remoteRegistrationId, byte[] contactInvitationToken, out UserId remoteUserIdNullable, out UserCertificate localUserCertificateWithPrivateKey, out bool autoReply)
        {
            remoteUserIdNullable = null;

            var contact = GetUnconfirmedContactByToken(contactInvitationToken);
            if (contact == null)
            {
                autoReply = false;
                localUserCertificateWithPrivateKey = null;
            }
            else
            {
                autoReply = true;
                localUserCertificateWithPrivateKey = User.LocalUserCertificate;
            }
        }
        Ike1Data IDrpRegisteredPeerApp.OnReceivedInvite_GetLocalIke1Data(byte[] contactInvitationToken)
        {
            var unconfirmedContact = GetUnconfirmedContactByToken(contactInvitationToken);
            if (unconfirmedContact == null) return null;
            return new Ike1Data { UserId = User.UserID, RegistrationIds = UserRegistrationIDs.Select(x => x.RegistrationId).ToArray() };
        }
        void IDrpRegisteredPeerApp.OnReceivedInvite_SetRemoteIke1Data(byte[] contactInvitationToken, Ike1Data remoteIke1Data)
        {
            var contact = GetUnconfirmedContactByToken(contactInvitationToken);
            if (contact == null) throw new BadSignatureException();

            contact.User = new User
            {
                OwnerLocalUserId = this.User.Id,
                AliasID = contact.UserAliasID, 
                UserID = remoteIke1Data.UserId,
                Metadata = new UserMetadata { ContactCreatedAtUTC = _userAppEngine.Engine.DateTimeNowUtc, ContactCreatedWithRemoteEndpoint = remoteIke1Data.RemoteEndPoint }
            };
            contact.RegistrationIDs = remoteIke1Data.RegistrationIds.Select(x => new UserRegistrationID { RegistrationId = x }).ToList();
            _userAppEngine.ConfirmContact(contact); // insert new records into database

            Contacts.Remove(contact.UnconfirmedContactId.Value);
            Contacts.Add(contact.User.Id, contact);
        }
        public void AddNewContact_RemotelyInitiated(string aliasID, string remotelyInitiatedIke1InvitationStr)
        {
            var localDrpPeer = UserRegistrationIDs.Select(x => x.LocalDrpPeer).FirstOrDefault();
            if (localDrpPeer == null) throw new InvalidOperationException("no local DRP peers found to send INVITE with contact invitation");

            var unconfirmedContactId = _unconfirmedContactIdCounter--;
            var remotelyInitiatedIke1Invitation = Ike1Invitation.DecodeFromUI(remotelyInitiatedIke1InvitationStr);
            var newContact = new Contact()
            {
                UnconfirmedContactId = unconfirmedContactId,
                UnconfirmedContactOwnerLocalUserId = User.Id,
                RemotelyInitiatedIke1Invitation = remotelyInitiatedIke1Invitation,
                UserAliasID = aliasID
            };
            Contacts.Add(unconfirmedContactId, newContact);

            localDrpPeer.BeginIke1(User.LocalUserCertificate, 
                new Ike1Data { UserId = User.UserID, RegistrationIds = UserRegistrationIDs.Select(x => x.RegistrationId).ToArray() }, 
                remotelyInitiatedIke1Invitation,
                TimeSpan.FromMinutes(10),
                (exc, remoteIke1Data) =>
                {
                    if (exc != null)
                    {
                        _userAppEngine.HandleException("IKE1 failed: ", exc);
                        return;
                    }

                    newContact.User = new User 
                    {
                        OwnerLocalUserId = this.User.Id,
                        AliasID = newContact.UserAliasID, 
                        UserID = remoteIke1Data.UserId,
                        Metadata = new UserMetadata { ContactCreatedAtUTC = _userAppEngine.Engine.DateTimeNowUtc, ContactCreatedWithRemoteEndpoint = remoteIke1Data.RemoteEndPoint }
                    };
                    newContact.RegistrationIDs = remoteIke1Data.RegistrationIds.Select(x => new UserRegistrationID { RegistrationId = x }).ToList();
                    _userAppEngine.ConfirmContact(newContact); // insert new records into database

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
    }
}
