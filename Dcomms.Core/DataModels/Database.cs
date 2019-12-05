using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SQLite;

namespace Dcomms.DataModels
{
    public class Database
    {
        public Database()
        {
            string dbPath = Path.Combine(
                 Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                 "ormdemo.db3");

            var db = new SQLiteConnection(dbPath);


            db.CreateTable<UserId>(CreateFlags.None);
            db.Insert(new UserId
            {
            }); // after creating the newStock object


            var user = db.Get<UserId>(5); // primary key id of 5
            var users = db.Table<UserId>();
        }
    }


    /// <summary>
    /// both local user IDs and remote user IDs (=contact book)
    /// </summary>
    [Table("UserIDs")]
    public class UserId
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public byte[]         UserID { get; set; }
        public string AliasID { get; set; }
        bool IsLocal { get; set; }
       // LocalUserCertificate
            //    PrivateKey
            //    PublicKey
           //     ValidPeriod
           //     RootUserKeySignature(s) (multiple)
    }
}
