using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dcomms.UserApp;
using Microsoft.AspNetCore.Mvc;

namespace Dcomms.MessengerT.Controllers
{
    public class ContactsController : Controller
    {
        public IActionResult Add(int id)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(id, out var localUser)) return NotFound();
            return View(localUser);
        }
        [HttpPost, ActionName("Add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddConfirmed(int id, string initiated, 
            [Bind("NewContactAliasID", "NewContact_LocallyInitiatedInvitationKey", "NewContact_RemotelyInitiatedInvitationKey")] LocalUser newFieldsUser
            )
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(id, out var localUser)) return NotFound();
            
            if (String.IsNullOrEmpty(newFieldsUser.NewContactAliasID))
                ModelState.AddModelError("NewContactAliasID", "Please enter contact ID");

            if (!ModelState.IsValid) return View(localUser);
         

            switch (initiated)
            {
                case "locally":
                    localUser.AddNewContact_LocallyInitiated(newFieldsUser.NewContactAliasID, newFieldsUser.NewContact_LocallyInitiatedInvitationKey);
                    break;
                case "remotely":
                    if (String.IsNullOrEmpty(newFieldsUser.NewContact_RemotelyInitiatedInvitationKey))
                        ModelState.AddModelError("NewContact_RemotelyInitiatedInvitationKey", "Please enter invitation key");
                    if (!ModelState.IsValid) return View(localUser);
                    localUser.AddNewContact_RemotelyInitiated(newFieldsUser.NewContactAliasID, newFieldsUser.NewContact_RemotelyInitiatedInvitationKey);
                    break;
            }
                       
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }
               
        public IActionResult Details(int userId, int contactId)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(userId, out var localUser)) return NotFound();
            if (!localUser.Contacts.TryGetValue(contactId, out var contact)) return NotFound();        
            return View(contact);
        }
        public IActionResult Delete(int userId, int contactId)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(userId, out var localUser)) return NotFound();
            if (!localUser.Contacts.TryGetValue(contactId, out var contact)) return NotFound();
            return View(contact);
        }
    }
}