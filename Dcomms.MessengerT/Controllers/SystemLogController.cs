using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        public IActionResult Index([Bind("DisplayedLogMessagesMaxCount,DisplayFilterModuleContainsStrings,ClearLog_MessagesCount,AttentionLevel,DisplayFilterMinLevel,DisplayFilterMessageContainsString")] Vision.VisionChannel1 model)
        {
            Program.VisionChannel.DisplayedLogMessagesMaxCount = model.DisplayedLogMessagesMaxCount;
            Program.VisionChannel.DisplayFilterModuleContainsStrings = model.DisplayFilterModuleContainsStrings;
            Program.VisionChannel.ClearLog_MessagesCount = model.ClearLog_MessagesCount;
            Program.VisionChannel.DisplayFilterMessageContainsString = model.DisplayFilterMessageContainsString;
                       

            Program.VisionChannel.AttentionLevel = model.AttentionLevel;
            Program.VisionChannel.DisplayFilterMinLevel = model.DisplayFilterMinLevel;


            return View(Program.VisionChannel);
        }
        public IActionResult Download()
        {
            var messages = Program.VisionChannel.GetLogMessages_newestFirst(null);

            byte[] zipData;

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    using (var entryStream = archive.CreateEntry("logs.txt").Open())
                    using (var streamWriter = new StreamWriter(entryStream))
                    {
                        foreach (var msg in messages)
                            streamWriter.Write($"{msg.TimeStr}\t{msg.ModuleName}\t{msg.AttentionLevelStr}\t{msg.SourceId}\t{msg.Message}\r\n");
                    }
                }

                zipData = memoryStream.ToArray();

                //using (var fileStream = new FileStream(@"C:\Temp\test.zip", FileMode.Create))
                //{
                //    memoryStream.Seek(0, SeekOrigin.Begin);
                //    memoryStream.CopyTo(fileStream);
                //}
            }



            //var zipMS = new MemoryStream();
            //byte[] zipData;
            //using (var zipArchive = new ZipArchive(zipMS, ZipArchiveMode.Create))
            //{
            //    var zipEntry = zipArchive.CreateEntry("log.txt");
            //    using (var zipStream = zipEntry.Open())
            //    {
            //        using (var writer = new StreamWriter(zipStream))
            //        {
            //            foreach (var msg in messages)
            //                writer.Write($"{msg.TimeStr}\t{msg.ModuleName}\t{msg.AttentionLevelStr}\t{msg.SourceId}\t{msg.Message}\r\n");
            //            writer.Flush();
            //            zipStream.Flush();
            //        }
            //    }
            //    zipData = zipMS.ToArray();
            //}


            return File(new MemoryStream(zipData), "application/zip", "Dcomms_logs.zip");

            //return new FileCallbackResult(new MediaTypeHeaderValue("application/zip"), async (outputStream, _) =>
            //{
            //    var zipMS = new MemoryStream();
            //    using (var zipArchive = new ZipArchive(//new WriteOnlyStreamWrapper(
            //        zipMS//)
            //        , ZipArchiveMode.Create))
            //    {

            //        var zipEntry = zipArchive.CreateEntry("log.txt");
            //        using (var zipStream = zipEntry.Open())
            //        {
            //            using (var writer = new StreamWriter(zipStream))
            //            {
            //                foreach (var msg in messages)
            //                    writer.Write($"{msg.TimeStr}\t{msg.ModuleName}\t{msg.AttentionLevelStr}\t{msg.SourceId}\t{msg.Message}\r\n");                         
            //            }
            //        }
            //        await zipMS.CopyToAsync(outputStream);            
            //    }
            //})
            //{
            //    FileDownloadName = "Dcomms_logs.zip"
            //};
        }

        public class FileCallbackResult : FileResult
        {
            private Func<Stream, ActionContext, Task> _callback;

            public FileCallbackResult(MediaTypeHeaderValue contentType, Func<Stream, ActionContext, Task> callback)
                : base(contentType?.ToString())
            {
                if (callback == null)
                    throw new ArgumentNullException(nameof(callback));
                _callback = callback;
            }

            public override Task ExecuteResultAsync(ActionContext context)
            {
                if (context == null)
                    throw new ArgumentNullException(nameof(context));
                var executor = new FileCallbackResultExecutor(context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>());
                return executor.ExecuteAsync(context, this);
            }

            private sealed class FileCallbackResultExecutor : FileResultExecutorBase
            {
                public FileCallbackResultExecutor(ILoggerFactory loggerFactory)
                    : base(CreateLogger<FileCallbackResultExecutor>(loggerFactory))
                {
                }

                public Task ExecuteAsync(ActionContext context, FileCallbackResult result)
                {                   
                    SetHeadersAndLog(context, result, null, false);
                    return result._callback(context.HttpContext.Response.Body, context);
                }
            }
        }
    }
}