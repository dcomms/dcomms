using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dcomms.MessengerT.Controllers
{
    public class LocalUsersController : Controller
    {
        public IActionResult Details(int id)
        {
            var localUser = Program.UserAppEngine.LocalUsers.FirstOrDefault(x => x.User.Id == id);
            if (localUser == null) return NotFound();
            return View(localUser);
        }
    }
}