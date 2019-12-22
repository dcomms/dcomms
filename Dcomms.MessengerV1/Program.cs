using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dcomms.UserApp;
using Dcomms.Vision;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dcomms.MessengerV1
{
    public class Program
    {
        public static VisionChannel1 VisionChannel;
        public static void Main(string[] args)
        {
            VisionChannel = new VisionChannel1()
            {
                AttentionLevel = AttentionLevel.deepDetail,
                DisplayFilterMinLevel = AttentionLevel.deepDetail,
                DisplayedLogMessagesMaxCount = 1000,
                ClearLog_RamSizeMB = 100,
                ClearLog_MessagesCount = 1000,
            };
            VisionChannel.SevereMessageEmitted += (msg) => Console.WriteLine(msg);
            
            try
            {
                VisionChannel.Emit("", "", AttentionLevel.higherLevelDetail, "creating user app...");
                using var userAppEngine = new UserAppEngine(VisionChannel);
                VisionChannel.Emit("", "", AttentionLevel.higherLevelDetail, "creating web host...");
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception exc)
            {
                VisionChannel.Emit("", "", AttentionLevel.strongPain, $"error in Program.Main(): {exc}");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    // clear all previously registered providers
                    logging.ClearProviders();

                    // now register everything you *really* want
                    //...
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseKestrel()
                        .ConfigureKestrel(serverOptions =>
                        {
                            //serverOptions.ListenLocalhost(7777);
                        })
                        .UseIISIntegration()
                        .CaptureStartupErrors(true)
                        .UseStartup<Startup>();
                });
    }
}
