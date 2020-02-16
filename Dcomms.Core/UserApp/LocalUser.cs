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
            if (String.IsNullOrEmpty(locallyInitiatedIke1Invitation))
                throw new ArgumentNullException(nameof(locallyInitiatedIke1Invitation));

            this.User.LocalUserCertificate.AssertHasPrivateKey();
            this.User.LocalUserCertificate.AssertIsValidNow(_userAppEngine.Engine.CryptoLibrary, User.UserID, _userAppEngine.Engine.DateTimeNowUtc);
            
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
            return Contacts.Values.FirstOrDefault(x => x.LocallyInitiatedIke1Invitation != null && MiscProcedures.EqualByteArrays(x.LocallyInitiatedIke1Invitation.ContactInvitationToken, contactInvitationToken) == true);
        }
        Contact GetContactByRegistrationId(RegistrationId remoteRegistrationId)
        {
            return Contacts.Values.FirstOrDefault(x => x.RegistrationIDs.Any(rid => rid.RegistrationId.Equals(remoteRegistrationId)));

        }

        void IDrpRegisteredPeerApp.OnReceivedInvite(RegistrationId remoteRegistrationId, byte[] contactInvitationToken, out UserId remoteUserIdNullable, out UserCertificate localUserCertificateWithPrivateKey, out bool autoReply)
        {
            remoteUserIdNullable = null;

            var contact = GetContactByRegistrationId(remoteRegistrationId);
            if (contact != null)
            {
                _userAppEngine.WriteToLog_higherLevelDetail($"confirmed contact '{contact.UserAliasID}' was found by registrationId={remoteRegistrationId}");
                autoReply = true;
                remoteUserIdNullable = contact.User.UserID;
                localUserCertificateWithPrivateKey = User.LocalUserCertificate;
                localUserCertificateWithPrivateKey.AssertHasPrivateKey();
                localUserCertificateWithPrivateKey.AssertIsValidNow(_userAppEngine.Engine.CryptoLibrary, User.UserID, _userAppEngine.Engine.DateTimeNowUtc);
            }
            else
            {
                contact = GetUnconfirmedContactByToken(contactInvitationToken);
                if (contact == null)
                {
                    _userAppEngine.WriteToLog_needsAtttention($"unconfirmed contact was not found by contactInvitationToken={MiscProcedures.ByteArrayToString(contactInvitationToken)}");
                    autoReply = false;
                    localUserCertificateWithPrivateKey = null;
                }
                else
                {
                    _userAppEngine.WriteToLog_higherLevelDetail($"unconfirmed contact '{contact.UserAliasID}' was found by contactInvitationToken={MiscProcedures.ByteArrayToString(contactInvitationToken)}");
                    autoReply = true;
                    localUserCertificateWithPrivateKey = User.LocalUserCertificate;
                    localUserCertificateWithPrivateKey.AssertHasPrivateKey();
                    localUserCertificateWithPrivateKey.AssertIsValidNow(_userAppEngine.Engine.CryptoLibrary, User.UserID, _userAppEngine.Engine.DateTimeNowUtc);
                }
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

            this.User.LocalUserCertificate.AssertHasPrivateKey();
            this.User.LocalUserCertificate.AssertIsValidNow(_userAppEngine.Engine.CryptoLibrary, User.UserID, _userAppEngine.Engine.DateTimeNowUtc);


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
            Contacts.Remove(contact.ContactId);          
            if (contact.IsConfirmed) 
                _userAppEngine.DeleteContact(contact);
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
            try
            {
                var contact = Contacts.Values.FirstOrDefault(x => x.RegistrationIDs.Any(rid => rid.RegistrationId.Equals(req.RequesterRegistrationId)));
                if (contact != null)
                {
                    var msg = new MessageForUI { Text = messageText, IsOutgoing = false, LocalCreationTimeUTC = _userAppEngine.Engine.DateTimeNowUtc_SystemClock };
                    contact.Messages.Add(msg);
                                      
                    _userAppEngine.WriteToLog_higherLevelDetail($"{msg} is being sent to {contact}. calling InvokeOnMessagesUpdated()");

                    _userAppEngine.InvokeOnMessagesUpdated(contact);
                }
                else throw new InvalidOperationException("contact was no found 234sdfs");
            }
            catch (Exception exc)
            {
                HandleException("error when processing message: ", exc);
            }
        }
        public void SendMessage(Contact contact, string message)
        {
            var localDrpPeer = UserRegistrationIDs.Where(x => x.LocalDrpPeer != null).Select(x => x.LocalDrpPeer).FirstOrDefault();
            if (localDrpPeer == null) throw new Exception("local DRP peer is null 123fdsf");
            var msg = new MessageForUI { Text = message, IsOutgoing = true, LocalCreationTimeUTC = _userAppEngine.Engine.DateTimeNowUtc_SystemClock };
            localDrpPeer.BeginSendShortSingleMessage(this.User.LocalUserCertificate,
                contact.RegistrationIDs.Select(x => x.RegistrationId).First(), 
                contact.User.UserID, message,
                TimeSpan.FromSeconds(60), (exc) =>
            {
                if (exc == null)
                {
                    msg.IsDelivered = true;
                    _userAppEngine.WriteToLog_higherLevelDetail($"{msg} is delivered to {contact}. calling InvokeOnMessagesUpdated()");
                    _userAppEngine.InvokeOnMessagesUpdated(contact);
                }
                //else   TODO168719
            });
            contact.Messages.Add(msg);
        }
    }
}
