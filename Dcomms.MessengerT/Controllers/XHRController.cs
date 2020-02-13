﻿using System;
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

        public IActionResult LocalUsersAndContacts()
        {
            return Json(Program.UserAppEngine.LocalUsers.Values.Select(x => new LocalUserForWebUI(x)).ToArray(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }



        public IActionResult Messages(int localUserId, int contactId)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(localUserId, out var localUser))
                return NotFound();
            if (!localUser.Contacts.TryGetValue(contactId, out var contact))
                return NotFound();

            //var r = new List<MessageForWebUI>();
            //for (int i = 0; i < 10; i++)
            //    r.Add(new MessageForWebUI { Text = $"msg{i} for contacct ID={contactId}" });

            return Json(contact.Messages.ToArray(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public IActionResult SendMessage(int localUserId, int contactId, string message)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(localUserId, out var localUser))
                return NotFound();
            if (!localUser.Contacts.TryGetValue(contactId, out var contact))
                return NotFound();

            localUser.SendMessage(contact, message);
                       
            return Ok();
        }

    }
}