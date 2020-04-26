using Dcomms.P2PTP;
using Dcomms.SUBT.GUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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

        #region show running instance
        public static void ShowRunningInstance()
        {
            try
            {
                File.WriteAllText(ShowRunningInstanceSignalFileName, DateTime.Now.ToString());
            }
            catch
            {

            }
        }
        static string _baseDirectoryCached;
        public static string BaseDirectory
        {
            get
            {
                if (_baseDirectoryCached == null)
                {
                    Uri dllURI = new Uri(Assembly.GetCallingAssembly().CodeBase);
                    _baseDirectoryCached = System.IO.Path.GetDirectoryName(dllURI.LocalPath);
                }
                return _baseDirectoryCached;
            }
        }
        static string ShowRunningInstanceSignalFileName => Path.Combine(BaseDirectory, "showrunninginstancesignal.txt");
        string ReadShowRunningInstanceSignal()
        {
            try 
            {
                return File.ReadAllText(ShowRunningInstanceSignalFileName);
            }
            catch
            {
                return "";
            }
        }
        string LatestShowRunningInstanceSignal;
        void ShowThisRunningInstanceIfNeeded()
        {
            var newSignal = ReadShowRunningInstanceSignal();
            if (LatestShowRunningInstanceSignal != newSignal)
            {
                LatestShowRunningInstanceSignal = newSignal;
                NotifyIcon_MouseDoubleClick(null, null);
            }
        }
        #endregion


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


            LatestShowRunningInstanceSignal = ReadShowRunningInstanceSignal();
            var timer = new DispatcherTimer(DispatcherPriority.SystemIdle);
            timer.Interval = TimeSpan.FromMilliseconds(300);
            timer.Tick += (s, e) => ShowThisRunningInstanceIfNeeded();
            timer.Start();
            _timers.Add(timer);
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
            if (_cstApp.AutoStartedInTrayMode) this.ShowInTaskbar = true;
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
        void ICstAppUser.ShowMessageToUser(string msg) => MessageBox.Show(msg);

        string ICstAppUser.CsvDelimiter => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        CultureInfo ICstAppUser.CsvCultureInfo => System.Globalization.CultureInfo.CurrentCulture;
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
                  
                    _cstApp.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.deepDetail, $"getting rule: netsh exited with code {netshProcess.ExitCode}. output:\r\n{stdOutput}");
                    ruleAlreadyExists = (netshProcess.ExitCode == 0) && stdOutput.Contains(processName);
                    _cstApp.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.deepDetail, $"ruleAlreadyExists={ruleAlreadyExists}");
                }
                catch (Exception exc)
                {
                    _cstApp.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.lightPain, $"could not get existing rule in firewall: {exc.Message}");
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
                        _cstApp.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.deepDetail, $"adding rule: netsh exited with code {netshProcess.ExitCode}");
                    else _cstApp.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.lightPain, $"adding rule: netsh exited with code {netshProcess.ExitCode}");
                    _cstApp.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.higherLevelDetail, $"successfully configured firewall");
                }
            }
            catch (Exception exc)
            {
                _cstApp?.VisionChannel?.Emit("", visionChannelModuleName, Dcomms.Vision.AttentionLevel.lightPain, $"could not configure firewall: {exc.Message}");
            }
        }
    }
}
