using Dcomms.SUBT.GUI;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace StarTrinity.ContinuousSpeedTest
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
#if !DEBUG
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                string currentProcessFileName = currentProcess.MainModule.FileName;
                foreach (var runningProcess in System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName))
                {
                    try
                    {
                        if (runningProcess.Id != currentProcess.Id)
                        {
                            if (runningProcess.MainModule.FileName == currentProcessFileName)
                            {
                                if (MessageBox.Show($"The application is already running from {currentProcessFileName}.\r\n\r\n" +
                                    $"Please open the running application in Windows tray bar.\r\n" +
                                    $"Do you want to run new instance instead of currently running instance?",
                                    "StarTrinity CST", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.No)
                                {
                                    // terminate this instance
                                    Environment.Exit(0);
                                    return;
                                }
                                else
                                {
                                    // terminate another instance
                                    runningProcess.Kill();
                                    goto _start;
                                }
                            }
                        }
                    }
                    catch// (Exception exc)
                    { // ignore: can do nothing, dont annoy user
                     //   HandleException(exc);
                    }
                }
            }
            catch// (Exception exc)
            {// ignore: can do nothing, dont annoy user
             //   HandleException(exc);
            }
#endif

     _start:
            Dcomms.MiscProcedures.Initialize(CompilationInfo.CompilationDateTimeUtc, new DateTime(2019, 05, 01));

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
            if (CstApp.CanHandleException) // when GUI for log is ready
                CstApp.HandleException(exc);
            else
            {
                var excString = exc.ToString();
                if (excString.Contains("netstandard,"))
                    MessageBox.Show("Error: .NET core is not installed. Please install latest .NET Framework.\r\n\r\n" + excString);
                else MessageBox.Show("Error: " + excString);
            }
        }
    }
}
