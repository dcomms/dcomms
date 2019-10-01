using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Dcomms.CryptographyTester
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionEventHandler);

            this.DispatcherUnhandledException += (sender, args) =>
            {
                HandleException(args.Exception);
                args.Handled = true;
            };

            base.OnStartup(e);
        }
        static void UnhandledExceptionEventHandler(object sender, UnhandledExceptionEventArgs args)
        {
            HandleException((Exception)args.ExceptionObject);
        }



        static void HandleException(Exception exc)
        {
         //   if (CstApp.CanHandleException) // when GUI for log is ready
         //       CstApp.HandleException(exc);
         //   else
            {
                var excString = exc.ToString();
                if (excString.Contains("netstandard,"))
                    MessageBox.Show("Error: .NET core is not installed. Please install latest .NET Framework.\r\n\r\n" + excString);
                else MessageBox.Show("Error: " + excString);
            }
        }

    }
}
