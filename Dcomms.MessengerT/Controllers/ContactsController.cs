using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        public async Task<IActionResult> AddConfirmed(int id)
        {
            if (!Program.UserAppEngine.LocalUsers.TryGetValue(id, out var localUser)) return NotFound();

            localUser.AddNewContact();

            return RedirectToAction(nameof(HomeController.Index), "Home");
        }
    }
}