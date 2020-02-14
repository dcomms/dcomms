using System;
using System.Collections.Generic;
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
            app.UseMiddleware<IpWhitelistMiddleware>();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapHub<Hub1>("/hub1");
            });

            app.Use(async (context, next) =>
            {
                var hub1Context = context.RequestServices
                                        .GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hub1>>();
                Program.UserAppEngine.OnMessagesUpdated += (contact) =>
                {
                    _ = hub1Context.Clients.All.SendCoreAsync("OnMessagesUpdated", new object[] { contact.OwnerLocalUserId, contact.ContactId });
                };
            });
        }
    }

    public class IpWhitelistMiddleware
    {
        private readonly RequestDelegate _next;

        public IpWhitelistMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (!remoteIp.Equals(IPAddress.Loopback) && !remoteIp.Equals(IPAddress.IPv6Loopback))
            {
                Console.WriteLine($"Forbidden HTTP request from {remoteIp}. Please access only from localhost");
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }
            await _next.Invoke(context);
        }
    }
}
