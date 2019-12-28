using Dcomms.UserApp.DataModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    public class Contact
    {
        public int? UnconfirmedContactId;
        public int ContactId => User != null ? User.Id : UnconfirmedContactId.Value;
        public int? UnconfirmedContactOwnerLocalUserId;
        public int OwnerLocalUserId => User != null ? User.OwnerLocalUserId : UnconfirmedContactOwnerLocalUserId.Value;
        public bool IsConfirmed => User != null;

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
        public User User; // = null if the contact is in "unconfirmed" state
        public List<UserRegistrationID> RegistrationIDs; // = null if the contact is in "unconfirmed" state

        public string LocallyGeneratedInvitationKey { get; set; } // is not null when the contact is in "pending" state and if the contact is initiated by local side
        public string RemotelyGeneratedInvitationKey { get; set; } // is not null when the contact is in "pending" state and if the contact is initiated by remote side
    }
}
