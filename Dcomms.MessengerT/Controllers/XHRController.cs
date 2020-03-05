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
            public bool ContainsUnreadMessages { get; set; }

            public ContactForWebUI(Contact contact)
            {
                Id = contact.ContactId;
                UserAliasID = contact.UserAliasID;
                ContainsUnreadMessages = contact.Messages.Any(x => x.IsUnread);
            }
        }
        public class LocalUserForWebUI
        {
            public int Id { get; set; }
            public string UserAliasID { get; set; }
            public ContactForWebUI[] Contacts { get; set; }
            public bool ContainsUnreadMessages { get; set; }
            public bool IsConnected { get; set; } 
            public LocalUserForWebUI(LocalUser localUser)
            {
                Id = localUser.User.Id;
                UserAliasID = localUser.UserAliasID;
                Contacts = localUser.Contacts.Values.Select(x => new ContactForWebUI(x)).OrderBy(x => x.ContainsUnreadMessages ? 0 : 1).ThenBy(x => x.UserAliasID).ToArray();
                ContainsUnreadMessages = Contacts.Any(x => x.ContainsUnreadMessages);
                if (localUser.UserRegistrationIDs != null)
                    IsConnected = localUser.UserRegistrationIDs.Any(rid => rid.LocalDrpPeer != null && rid.LocalDrpPeer.IsConnected);
            }
        }

        public IActionResult LocalUsersAndContacts()
        {
            return Json(Program.UserAppEngine.LocalUsers.Values.Select(x => new LocalUserForWebUI(x)).OrderBy(x => x.ContainsUnreadMessages ? 0 : 1).ThenBy(x => x.UserAliasID).ToArray(), new JsonSerializerOptions
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

            var r = contact.Messages.ToArray();
            foreach (var msg in r) msg.IsUnread = false;
            Program.UserAppEngine.WriteToLog_higherLevelDetail($"XHR/Messages returns {String.Join(';', r.Select(x => x.ToString()))}");
            return Json(new { messages = r, messagesVersion = contact.MessagesVersion }, new JsonSerializerOptions
            {
                WriteIndented = true
            }
            );
        }
        public IActionResult MessagesVersion(int localUserId, int contactId)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(localUserId, out var localUser))
                return NotFound();
            if (!localUser.Contacts.TryGetValue(contactId, out var contact))
                return NotFound();

            Program.UserAppEngine.WriteToLog_higherLevelDetail($"XHR/MessagesVersion returns {contact.MessagesVersion}");
            return Json(new { messagesVersion = contact.MessagesVersion }, new JsonSerializerOptions
            {
                WriteIndented = true
            }
            );
        }

        public IActionResult SendMessage(int localUserId, int contactId, string message)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(localUserId, out var localUser))
                return NotFound();
            if (!localUser.Contacts.TryGetValue(contactId, out var contact))
                return NotFound();

            try
            {
                localUser.SendMessage(contact, message);
                return Json(new { success = true });
            }
            catch (Exception exc)
            {
                Program.UserAppEngine.HandleException($"can not send message to {contact}: ", exc);
                return Json(new { success = false, errorDescription = exc.Message });
            }
        }

    }
}