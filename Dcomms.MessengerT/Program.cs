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

namespace Dcomms.MessengerT
{
    public class Program
    {
        const string Url = "http://localhost:5050";
        public static VisionChannel1 VisionChannel;
        public static UserAppEngine UserAppEngine;
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
                VisionChannel.Emit("", "", AttentionLevel.higherLevelDetail, "creating user app");
                UserAppEngine = new UserAppEngine(VisionChannel, null);
                VisionChannel.Emit("", "", AttentionLevel.higherLevelDetail, $"creating web host at {Url}");
                var host = CreateHostBuilder(Url, args).Build();
                VisionChannel.Emit("", "", AttentionLevel.higherLevelDetail, $"created web host at {Url}");

                Console.WriteLine($"please access messenger web UI from same machine in browser: {Url}");

                VisionChannel.Emit("", "", AttentionLevel.higherLevelDetail, $"running web host at {Url}");
                host.Run();
                UserAppEngine?.Dispose();
            }
            catch (Exception exc)
            {
                VisionChannel.Emit("", "", AttentionLevel.strongPain, $"error in Program.Main(): {exc}");
                UserAppEngine?.Dispose();
            }
        }

        public static IHostBuilder CreateHostBuilder(string url, string[] args) =>
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
                        })
                      //  .UseIISIntegration()
                     //   .CaptureStartupErrors(true)
                        .UseUrls(url)
                        .UseStartup<Startup>();
                });
    }
}
