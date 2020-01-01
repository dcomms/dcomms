using Dcomms.DMP;
using SQLite;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.UserApp.DataModels
{

    /// <summary>
    /// both local user IDs and remote user IDs (=contact book)
    /// </summary>
    [Table("Users")]
    public class User
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int OwnerLocalUserId { get; set; } // = 0 means NULL (this user is local)

        [Ignore]
        public UserId UserID { get; set; }
        public byte[] UserID_encrypted { get; set; }
        public byte[] UserID_hmac { get; set; }

        [Ignore]        
        public string AliasID { get; set; }
        public byte[] AliasID_encrypted { get; set; }
        public byte[] AliasID_hmac { get; set; }

        [Ignore]
        public bool IsLocal => LocalUserCertificate_encrypted != null;
        [Ignore]
        public UserCertificate LocalUserCertificate { get; set; } // if not null then must contain provate key
        public byte[] LocalUserCertificate_encrypted { get; set; }
        public byte[] LocalUserCertificate_hmac { get; set; }
        
        [Ignore]
        public UserMetadata Metadata { get; set; }
        public byte[] Metadata_encrypted { get; set; }
        public byte[] Metadata_hmac { get; set; }

        public override string ToString() => AliasID;
    }

}
