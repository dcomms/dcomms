using Dcomms.DRP;
using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp.DataModels
{

    [Table("UserRegistrationIDs")]
    public class UserRegistrationID
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int UserId { get; set; } // FK to Users table

        [Ignore]
        public RegistrationId RegistrationId { get; set; }
        public byte[] RegistrationId_encrypted { get; set; }
        public byte[] RegistrationId_hmac { get; set; }

        
        [Ignore]
        public RegistrationPrivateKey RegistrationPrivateKey { get; set; } // is null for remote users
        public byte[] RegistrationPrivateKey_encrypted { get; set; }
        public byte[] RegistrationPrivateKey_hmac { get; set; }

        public LocalDrpPeer LocalDrpPeer;

    }
}
