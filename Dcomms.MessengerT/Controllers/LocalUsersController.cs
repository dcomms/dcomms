using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dcomms.UserApp;
using Dcomms.UserApp.DataModels;
using Microsoft.AspNetCore.Mvc;

namespace Dcomms.MessengerT.Controllers
{
    public class LocalUsersController : Controller
    {
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create([Bind("AliasID")] User user)
        {
            if (String.IsNullOrEmpty(user.AliasID))
                ModelState.AddModelError("AliasID", "Please enter account ID");
            
            if (ModelState.IsValid)
            {
                Program.UserAppEngine.AddLocalUser(user.AliasID);
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
            return View(user);
        }

        public IActionResult Details(int id)
        {
            var localUser = Program.UserAppEngine.LocalUsers.FirstOrDefault(x => x.User.Id == id);
            if (localUser == null) return NotFound();
            return View(localUser);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Details(int id, [Bind("UserAliasID")] LocalUser newFieldsUser)
        {
            if (String.IsNullOrEmpty(newFieldsUser.UserAliasID))
                ModelState.AddModelError("UserAliasID", "Please enter account ID");

            var localUser = Program.UserAppEngine.LocalUsers.FirstOrDefault(x => x.User.Id == id);
            if (localUser == null) return NotFound();
            if (ModelState.IsValid)
            {
                Program.UserAppEngine.UpdateLocalUser(localUser, newFieldsUser);                  
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }

            newFieldsUser.UserRegistrationIDs = localUser.UserRegistrationIDs;
            newFieldsUser.RootUserKeys = localUser.RootUserKeys;
            newFieldsUser.User = localUser.User;
            return View(newFieldsUser);
        }



        public IActionResult Delete(int id)
        {
            var localUser = Program.UserAppEngine.LocalUsers.FirstOrDefault(x => x.User.Id == id);
            if (localUser == null) return NotFound();
            return View(localUser);
        }
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var localUser = Program.UserAppEngine.LocalUsers.FirstOrDefault(x => x.User.Id == id);
            if (localUser == null) return NotFound();
            Program.UserAppEngine.DeleteLocalUser(localUser);
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }
    }
}