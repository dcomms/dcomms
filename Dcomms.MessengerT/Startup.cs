using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dcomms.MessengerT
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
            services.AddSignalR();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }
            app.UseMiddleware<Middleware1>();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }

    /// <summary>
    /// IP whitelist; logging
    /// </summary>
    public class Middleware1
    {
        private readonly RequestDelegate _next;

        public Middleware1(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (!remoteIp.Equals(IPAddress.Loopback) && !remoteIp.Equals(IPAddress.IPv6Loopback))
            {
                var msg = $"Forbidden HTTP request from {remoteIp}. Please access only from localhost";
                Program.UserAppEngine.WriteToLog_lightPain(msg);
                Console.WriteLine(msg);
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }
            var absoluteUri = string.Concat(
                        context.Request.Scheme,
                        "://",
                        context.Request.Host.ToUriComponent(),
                        context.Request.PathBase.ToUriComponent(),
                        context.Request.Path.ToUriComponent(),
                        context.Request.QueryString.ToUriComponent());
            Program.UserAppEngine.WriteToLog_higherLevelDetail($"processing HTTP request {absoluteUri}");
            var sw = Stopwatch.StartNew();
            try
            {
                await _next.Invoke(context);
                Program.UserAppEngine.WriteToLog_higherLevelDetail($"processed HTTP request {absoluteUri} in {sw.Elapsed.TotalMilliseconds}ms");
            }
            catch (Exception exc)
            {
                Program.UserAppEngine.WriteToLog_mediumPain($"error when processing HTTP request {absoluteUri}: {exc}");
                throw;
            }
        }
    }
}
