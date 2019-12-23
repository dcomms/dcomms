using Dcomms.P2PTP;
using Dcomms.SUBT.GUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace StarTrinity.ContinuousSpeedTest
{
    public partial class MainWindow : Window, ICstAppUser
    {
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        readonly CstApp _cstApp;
        bool _exitMenuItemClicked;

        public MainWindow()
        {
            _cstApp = new CstApp(this);
            this.DataContext = _cstApp;
            InitializeComponent();
            this.Title += " version " + CompilationInfo.CompilationDateTimeUtcStr;

            if (_cstApp.AutoStartedInTrayMode)
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                _notifyIcon.Text = this.Title;
                var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/StarTrinity.ContinuousSpeedTest;component/icon.ico")).Stream;
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream);

                
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                var menuItem1 = new System.Windows.Forms.ToolStripMenuItem("E&xit");
               
                menuItem1.Click += (s, e) => { _exitMenuItemClicked = true; this.Close(); };
                contextMenu.Items.AddRange(new [] { menuItem1 });


                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseDoubleClick);
                this.WindowState = WindowState.Minimized;
                this.Hide();
                _notifyIcon.Visible = true;
                this.ShowInTaskbar = false;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            foreach (var timer in _timers)
            {
                timer.Stop();
            }
            _cstApp.Dispose();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }

        void NotifyIcon_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            this.Show();
            this.Activate();
            this.ShowInTaskbar = true;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (_cstApp.AutoStartedInTrayMode)
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.ShowInTaskbar = false;
                    _notifyIcon.BalloonTipTitle = this.Title;
                    _notifyIcon.BalloonTipText = "running in background";
                    _notifyIcon.ShowBalloonTip(400);
                }
                else
                {
                    this.ShowInTaskbar = true;
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_cstApp.AutoStartedInTrayMode && _exitMenuItemClicked == false)
            {
                e.Cancel = true;
                this.WindowState = WindowState.Minimized;
            }
        }

        readonly List<DispatcherTimer> _timers = new List<DispatcherTimer>();
        void ICstAppUser.CreateIdleGuiTimer(TimeSpan interval, Action cb)
        {
            var timer = new DispatcherTimer(DispatcherPriority.SystemIdle);
            timer.Interval = interval;
            timer.Tick += (s,e) => cb();
            timer.Start();
            _timers.Add(timer);
        }
        void ICstAppUser.AddStaticResource(string name, object value)
        {
            if (Application.Current != null) Application.Current.Resources.Add(name, value);
        }
        #region installation
        const string AppNameInRegistry = "StarTrinity CST";
        //public bool InstalledOnThisPc_AutostartInTrayMode
        //{
        //    get
        //    {
        //        try
        //        {
        //            var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
        //            var v = rk.GetValue(AppNameInRegistry);
        //            return v != null;
        //        }
        //        catch (Exception exc)
        //        {
        //            HandleException(exc);
        //            return false;
        //        }
        //    }
        //}

        string CurrentProcessDirectory => Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        string LocalPcInstallationFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarTrinity CST");
        public bool RunningInstalledOnThisPC
        {
            get
            {
                return CurrentProcessDirectory == LocalPcInstallationFolder;
            }
        }
        string DesktopShortcutFileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "StarTrinity CST.lnk");
        string StartMenuShortcutFileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "StarTrinity CST.lnk");
        
        string ICstAppUser.CsvDelimiter => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        CultureInfo ICstAppUser.CsvCultureInfo => System.Globalization.CultureInfo.CurrentCulture;

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        internal class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        internal interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
        /// <summary>
        /// copies files from current folder to "app data" folder, if not 
        /// </summary>
        void ICstAppUser.InstallOnThisPC()
        {
            try
            {
                string mainExeFileName;
                bool closeThisProcess = false;
                // copy files to LocalPcInstallationFolder
                if (!RunningInstalledOnThisPC)
                {
                    var localPcInstallationFolder = LocalPcInstallationFolder;
                    if (!Directory.Exists(localPcInstallationFolder)) Directory.CreateDirectory(localPcInstallationFolder);
                    var currentProcessDirectory = CurrentProcessDirectory;
                    foreach (var dllFileName in Directory.GetFiles(currentProcessDirectory, "*.*")
                        .Select(x => x.ToLower())
                        .Where(s => s.EndsWith(".config") || s.EndsWith(".dll") || s.EndsWith(".exe") || s.EndsWith(".json")))
                        File.Copy(dllFileName, Path.Combine(localPcInstallationFolder, Path.GetFileName(dllFileName)), true);

                    mainExeFileName = Path.Combine(localPcInstallationFolder, Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName));
                    File.Copy(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, mainExeFileName, true);

                    // create shortcut on desktop
                    var link = (IShellLink)new ShellLink();
                    link.SetDescription("StarTrinity Continuous Speed Test");
                    link.SetPath(mainExeFileName);
                    var linkFile = (IPersistFile)link;
                    linkFile.Save(DesktopShortcutFileName, false);

                    // create icon in start menu
                    linkFile.Save(StartMenuShortcutFileName, false);

                    // show message box
                    MessageBox.Show($"Installation succeeded.\r\nPress OK to start the new installed program.\r\n\r\nInstallation folder: {localPcInstallationFolder}");

                    // start new process
                    System.Diagnostics.Process.Start(mainExeFileName);

                    closeThisProcess = true;
                }
                else
                    mainExeFileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

                if (_cstApp.InstallOnThisPC_AddToAutoStart)
                {
                    var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    rk.SetValue(AppNameInRegistry, mainExeFileName + " " + CstApp.TrayCliParameter);
                }

                if (closeThisProcess)
                    System.Windows.Application.Current.Shutdown();
                // RaisePropertyChanged(() => InstalledOnThisPcAndAutostartInTrayMode);
            }
            catch (Exception exc)
            {
                CstApp.HandleException(exc);
            }
        }
        void ICstAppUser.UninstallOnThisPC()
        {

            if (MessageBox.Show($"Do you really want to uninstall the software and all files in directory {CurrentProcessDirectory}?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.Yes)
            {

                try
                {
                    var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    rk.DeleteValue(AppNameInRegistry, false);
                }
                catch (Exception exc)
                {
                    CstApp.HandleException(exc);
                }

                // delete shortcut on desktop
                try
                {
                    if (File.Exists(DesktopShortcutFileName))
                        File.Delete(DesktopShortcutFileName);
                }
                catch (Exception exc)
                {
                    CstApp.HandleException(exc);
                }

                // delete folder in start menu
                try
                {
                    if (File.Exists(StartMenuShortcutFileName))
                        File.Delete(StartMenuShortcutFileName);
                }
                catch (Exception exc)
                {
                    CstApp.HandleException(exc);
                }

                // create bat               
                // remove files from current folder
                var batFileName = Path.Combine(Path.GetTempPath(), "startrinity_cst_uninstall.bat");
                if (File.Exists(batFileName)) File.Delete(batFileName);

                var batScript = new StringBuilder();
                batScript.Append("echo off\r\n");
                batScript.Append("echo uninstalling software...\r\n");

                batScript.AppendFormat(":retry\r\n");
                batScript.AppendFormat("del /S /Q /F \"{0}\\*\"\r\n", CurrentProcessDirectory);
                batScript.AppendFormat("rmdir \"{0}\"\r\n", CurrentProcessDirectory);
                //    batScript.AppendFormat("echo result: %ERRORLEVEL%\r\n");
                batScript.AppendFormat("@if exist \"{0}\" (\r\n", CurrentProcessDirectory);
                batScript.AppendFormat("  echo failed to remove files in '{0}'. trying again..\r\n", CurrentProcessDirectory);
                batScript.Append("  timeout /t 2\r\n"); //   wait 2 sec, retry
                batScript.AppendFormat("  goto retry\r\n");
                batScript.Append(")\r\n");


                File.WriteAllText(batFileName, batScript.ToString());

                // run bat
                System.Diagnostics.Process.Start(batFileName);

                // close this process
                System.Windows.Application.Current.Shutdown();

                // RaisePropertyChanged(() => InstalledOnThisPcAndAutostartInTrayMode);
            }

        }
        #endregion


        void ICstAppUser.ShowMessageToUser(string msg) => MessageBox.Show(msg);

        bool ICstAppUser.ShowSaveFileDialog(string fileExtension, out string fileName, out Action optionalFileWrittenCallback)
        {
            optionalFileWrittenCallback = null;
            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = $"{fileExtension.ToUpper()} files|*.{fileExtension.ToLower()}";
            if (dlg.ShowDialog() == true)
            {
                fileName = dlg.FileName;
                return true;
            }
            else
            {
                fileName = null;
                return false;
            }
        }

        public void ConfigureFirewallIfNotConfigured()
        {
            const string visionChannelModuleName = "firewall";
            try
            {
                const string ruleName = "StarTrinity CST";
                var processName = Process.GetCurrentProcess().MainModule.FileName;

                bool ruleAlreadyExists = false;

                try
                {
                    var netshProcess = new Process();
                    netshProcess.StartInfo.FileName = "netsh";
                    netshProcess.StartInfo.Arguments = $"advfirewall firewall show rule name=\"{ruleName}\" verbose";
                //    netshProcess.StartInfo.Verb = "runas"; 
                    netshProcess.StartInfo.UseShellExecute = false;
                    netshProcess.StartInfo.CreateNoWindow = true;
                    netshProcess.StartInfo.RedirectStandardOutput = true;
                  //  netshProcess.StartInfo.StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);  //this leads to error               
                    netshProcess.Start();

                    var stdOutput = "";                  
                    try
                    {
                        stdOutput = netshProcess.StandardOutput.ReadToEnd();
                    }
                    catch
                    {
                        stdOutput = "";
                    }

                    netshProcess.WaitForExit();
                  
                    _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.deepDetail, $"getting rule: netsh exited with code {netshProcess.ExitCode}. output:\r\n{stdOutput}");
                    ruleAlreadyExists = (netshProcess.ExitCode == 0) && stdOutput.Contains(processName);
                    _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.deepDetail, $"ruleAlreadyExists={ruleAlreadyExists}");
                }
                catch (Exception exc)
                {
                    _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.lightPain, $"could not get existing rule in firewall: {exc.Message}");
                }


                if (ruleAlreadyExists == false)
                {
                    var netshProcess = new Process();
                    netshProcess.StartInfo.FileName = "netsh";
                    netshProcess.StartInfo.Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow program=\"{processName}\" enable=yes";
                    netshProcess.StartInfo.Verb = "runas";
                    netshProcess.StartInfo.UseShellExecute = true;
                    netshProcess.Start();
                    netshProcess.WaitForExit();
                    if (netshProcess.ExitCode == 0)
                        _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.deepDetail, $"adding rule: netsh exited with code {netshProcess.ExitCode}");
                    else _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.lightPain, $"adding rule: netsh exited with code {netshProcess.ExitCode}");
                    _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.higherLevelDetail, $"successfully configured firewall");
                }
            }
            catch (Exception exc)
            {
                _cstApp.VisionChannel.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.lightPain, $"could not configure firewall: {exc.Message}");
            }
        }
    }
}
