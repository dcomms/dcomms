using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dcomms.UserApp;
using Microsoft.AspNetCore.Mvc;

namespace Dcomms.MessengerT.Controllers
{
    /// <summary>
    /// contains handlers of browser-side AJAX requests - XMLHttpRequests (XHRs)
    /// </summary>
    public class XHRController : Controller
    {
        public class ContactForWebUI
        {
            public int Id { get; set; }
            public string UserAliasID { get; set; }
          //  public int OwnerLocalUserId { get; set; }

            public ContactForWebUI(Contact contact)
            {
                Id = contact.ContactId;
                UserAliasID = contact.UserAliasID;
            //    OwnerLocalUserId = contact.OwnerLocalUserId;
            }
        }
        public class LocalUserForWebUI
        {
            public int Id { get; set; }
            public string UserAliasID { get; set; }
            public ContactForWebUI[] Contacts { get; set; }
            public LocalUserForWebUI(LocalUser localUser)
            {
                Id = localUser.User.Id;
                UserAliasID = localUser.UserAliasID;
                Contacts = localUser.Contacts.Values.Select(x => new ContactForWebUI(x)).ToArray();
            }
        }

        private IEnumerable<ContactForWebUI> GetInternal()
        {
            foreach (var u in Program.UserAppEngine.LocalUsers.Values)
                foreach (var c in u.Contacts.Values)
                    yield return new ContactForWebUI(c);
        }
        public IActionResult LocalUsersAndContacts()
        {
            return Json(Program.UserAppEngine.LocalUsers.Values.Select(x => new LocalUserForWebUI(x)).ToArray(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

    }
}