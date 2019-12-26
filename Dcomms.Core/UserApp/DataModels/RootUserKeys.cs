using Dcomms.DMP;
using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp.DataModels
{
    [Table("RootUserKeys")]
    public class RootUserKeys
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
               
        [Indexed]
        public int UserId { get; set; } // FK to Users table
        
        [Ignore]
        public UserRootPrivateKeys UserRootPrivateKeys { get; set; }
        public byte[] UserRootPrivateKeys_encrypted { get; set; }
        public byte[] UserRootPrivateKeys_hmac { get; set; }
    }
}
