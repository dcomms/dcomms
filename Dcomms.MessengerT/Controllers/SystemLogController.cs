using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dcomms.MessengerT.Controllers
{
    public class SystemLogController : Controller
    {
        public IActionResult Index()
        {
            return View(Program.VisionChannel);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index([Bind("DisplayedLogMessagesMaxCount,DisplayFilterModuleContainsStrings,ClearLog_MessagesCount,AttentionLevel,DisplayFilterMinLevel")] Vision.VisionChannel1 model)
        {
            Program.VisionChannel.DisplayedLogMessagesMaxCount = model.DisplayedLogMessagesMaxCount;
            Program.VisionChannel.DisplayFilterModuleContainsStrings = model.DisplayFilterModuleContainsStrings;
            Program.VisionChannel.ClearLog_MessagesCount = model.ClearLog_MessagesCount;

            Program.VisionChannel.AttentionLevel = model.AttentionLevel;
            Program.VisionChannel.DisplayFilterMinLevel = model.DisplayFilterMinLevel;


            return View(Program.VisionChannel);
        }
    }
}