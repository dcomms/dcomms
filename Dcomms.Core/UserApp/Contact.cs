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

        /// <summary>
        /// is not null when the contact is in "pending" state and if the contact is initiated by local side
        /// is not saved to database
        /// </summary>
        public Ike1Invitation LocallyInitiatedIke1Invitation { get; set; }
        /// <summary>
        /// is not null when the contact is in "pending" state and if the contact is initiated by remote side
        /// is not saved to database
        /// </summary>
        public Ike1Invitation RemotelyInitiatedIke1Invitation { get; set; }
    }
}
