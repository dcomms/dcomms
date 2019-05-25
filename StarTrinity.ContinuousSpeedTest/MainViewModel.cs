using Dcomms.P2PTP.LocalLogic;
using Dcomms.SUBT;
using Dcomms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace StarTrinity.ContinuousSpeedTest
{
    public class MainViewModel : BaseNotify, IDisposable, ILocalPeerUser
    {
        internal const float InitialBandwidthTarget = 200 * 1024;

        bool _developerMode
#if DEBUG
            = true;
#else
            = false;
#endif
        public bool DeveloperMode
        {
            get => _developerMode;
            set
            {
                _developerMode = value;
                RaisePropertyChanged(() => DeveloperMode);
            }
        }

        internal bool AutoStartedInTrayMode { get; set; }
        const string TrayCliParameter = "/tray";

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
        public bool InstallOnThisPC_AddToAutoStart { get; set; } = true;
        string DesktopShortcutFileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "StarTrinity CST.lnk");
        string StartMenuShortcutFileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "StarTrinity CST.lnk");
        /// <summary>
        /// copies files from current folder to "app data" folder, if not 
        /// </summary>
        public ICommand InstallOnThisPC => new DelegateCommand(() =>
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
                    foreach (var dllFileName in Directory.GetFiles(currentProcessDirectory, "*.*").Where(s => s.EndsWith(".config") || s.EndsWith(".dll")))
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

                if (InstallOnThisPC_AddToAutoStart)
                {
                    var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    rk.SetValue(AppNameInRegistry, mainExeFileName + " " + TrayCliParameter);
                }

                if (closeThisProcess)
                    System.Windows.Application.Current.Shutdown();
               // RaisePropertyChanged(() => InstalledOnThisPcAndAutostartInTrayMode);
            }
            catch (Exception exc)
            {
                HandleException(exc);
            }
        });

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
        public ICommand UninstallOnThisPc => new DelegateCommand(() =>
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
                    HandleException(exc);
                }

                // delete shortcut on desktop
                if (File.Exists(DesktopShortcutFileName))
                    File.Delete(DesktopShortcutFileName);

                // delete folder in start menu
                if (File.Exists(StartMenuShortcutFileName))
                    File.Delete(StartMenuShortcutFileName);


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
        });
        #endregion

        #region configuration
        public DelegateCommand PredefinedAsvServer => new DelegateCommand(() =>
        {
            LocalPeerConfiguration.Coordinators = new IPEndPoint[0];
            LocalPeerConfiguration.SocketsCount = 8;
            LocalPeerConfigurationRoleAsUser = false;
            LocalPeerConfiguration.RoleAsSharedPassive = true;
            LocalPeerConfiguration.RoleAsCoordinator = true;
            LocalPeerConfiguration.LocalUdpPortRangeStart = 10000;
            RaisePropertyChanged(() => LocalPeerConfiguration);
            Initialize.Execute(null);
        });
                
        public DelegateCommand PredefinedAsvClientToNeth3 => new DelegateCommand(() =>
        {
            var coordinatorServerIp = IPAddress.Parse("163.172.210.13");
            LocalPeerConfiguration.Coordinators = new IPEndPoint[]
                {
                    new IPEndPoint(coordinatorServerIp, 10000),
                    new IPEndPoint(coordinatorServerIp, 10001),
                    new IPEndPoint(coordinatorServerIp, 10002),
                    new IPEndPoint(coordinatorServerIp, 10003),
                    new IPEndPoint(coordinatorServerIp, 10004),
                    new IPEndPoint(coordinatorServerIp, 10005),
                    new IPEndPoint(coordinatorServerIp, 10006),
                    new IPEndPoint(coordinatorServerIp, 10007),
                };
            LocalPeerConfiguration.SocketsCount = 4;
            LocalPeerConfiguration.LocalUdpPortRangeStart = null;
            LocalPeerConfigurationRoleAsUser = true;
            LocalPeerConfiguration.RoleAsSharedPassive = false;
            LocalPeerConfiguration.RoleAsCoordinator = false;
            RaisePropertyChanged(() => LocalPeerConfiguration);
            SubtLocalPeerConfigurationBandwidthTarget = InitialBandwidthTarget;
            Initialize.Execute(null);
        });
        public DelegateCommand PredefinedAsvPassiveClientToNeth3 => new DelegateCommand(() =>
        {
            var coordinatorServerIp = IPAddress.Parse("163.172.210.13");
            LocalPeerConfiguration.Coordinators = new IPEndPoint[]
                {
                    new IPEndPoint(coordinatorServerIp, 10000),
                    new IPEndPoint(coordinatorServerIp, 10001),
                    new IPEndPoint(coordinatorServerIp, 10002),
                    new IPEndPoint(coordinatorServerIp, 10003),
                    new IPEndPoint(coordinatorServerIp, 10004),
                    new IPEndPoint(coordinatorServerIp, 10005),
                    new IPEndPoint(coordinatorServerIp, 10006),
                    new IPEndPoint(coordinatorServerIp, 10007),
                };
            LocalPeerConfiguration.SocketsCount = 8;
            LocalPeerConfiguration.LocalUdpPortRangeStart = null;
            LocalPeerConfigurationRoleAsUser = false;
            LocalPeerConfiguration.RoleAsSharedPassive = true;
            LocalPeerConfiguration.RoleAsCoordinator = false;
            RaisePropertyChanged(() => LocalPeerConfiguration);
            RaisePropertyChanged(() => SubtLocalPeerConfiguration);
            Initialize.Execute(null);
        });
        public DelegateCommand PredefinedAsvClientToLocalhost => new DelegateCommand(() =>
        {
            var coordinatorServerIp = IPAddress.Parse("127.0.0.1");
            LocalPeerConfiguration.Coordinators = new IPEndPoint[]
                {
                    new IPEndPoint(coordinatorServerIp, 10000),
                    new IPEndPoint(coordinatorServerIp, 10001),
                };
            LocalPeerConfiguration.SocketsCount = 1;
            LocalPeerConfiguration.LocalUdpPortRangeStart = null;
            LocalPeerConfigurationRoleAsUser = true;
            LocalPeerConfiguration.RoleAsSharedPassive = false;
            LocalPeerConfiguration.RoleAsCoordinator = false;
            RaisePropertyChanged(() => LocalPeerConfiguration);
            SubtLocalPeerConfigurationBandwidthTarget = InitialBandwidthTarget;
            Initialize.Execute(null);
        });        
        public DelegateCommand PredefinedReleaseMode => new DelegateCommand(() => // multiple initial coordinators
        {
            var coordinatorServerIp1 = IPAddress.Parse("163.172.210.13");//neth3
            var coordinatorServerIp2 = IPAddress.Parse("195.154.173.208");//fra2
            LocalPeerConfiguration.Coordinators = new IPEndPoint[]
                {
                    new IPEndPoint(coordinatorServerIp1, 10000),
                    new IPEndPoint(coordinatorServerIp1, 10001),
                    new IPEndPoint(coordinatorServerIp1, 10002),
                    new IPEndPoint(coordinatorServerIp1, 10003),
                    new IPEndPoint(coordinatorServerIp1, 10004),
                    new IPEndPoint(coordinatorServerIp1, 10005),
                    new IPEndPoint(coordinatorServerIp1, 10006),
                    new IPEndPoint(coordinatorServerIp1, 10007),
                    new IPEndPoint(coordinatorServerIp1, 9000),
                    new IPEndPoint(coordinatorServerIp1, 9001),
                    new IPEndPoint(coordinatorServerIp1, 9002),
                    new IPEndPoint(coordinatorServerIp1, 9003),
                    new IPEndPoint(coordinatorServerIp2, 9000),
                    new IPEndPoint(coordinatorServerIp2, 9001),
                    new IPEndPoint(coordinatorServerIp2, 9002),
                    new IPEndPoint(coordinatorServerIp2, 9003),
                };
            LocalPeerConfiguration.LocalUdpPortRangeStart = null;
            LocalPeerConfigurationRoleAsUser = true;
            LocalPeerConfiguration.RoleAsSharedPassive = false;
            LocalPeerConfiguration.RoleAsCoordinator = false;
            RaisePropertyChanged(() => LocalPeerConfiguration);
            LocalPeerConfiguration.SocketsCount = 4;
            //SubtLocalPeerConfiguration.BandwidthLimitMbps = 3;
            RaisePropertyChanged(() => SubtLocalPeerConfiguration);
        });

        public LocalPeerConfiguration LocalPeerConfiguration { get; private set; } = new LocalPeerConfiguration() { RoleAsUser = true };
        public bool LocalPeerConfigurationRoleAsUser
        {
            get => LocalPeerConfiguration.RoleAsUser;
            set
            {
                LocalPeerConfiguration.RoleAsUser = value;
                RaisePropertyChanged(() => LocalPeerConfigurationRoleAsUser);
            }
        }

        public SubtLocalPeerConfiguration SubtLocalPeerConfiguration { get; private set; } = new SubtLocalPeerConfiguration() { BandwidthTarget = InitialBandwidthTarget };
        internal float SubtLocalPeerConfigurationBandwidthTarget
        {
            get
            {
                return SubtLocalPeerConfiguration.BandwidthTarget;
            }
            set
            {
                SubtLocalPeerConfiguration.BandwidthTarget = value;
                RaisePropertyChanged(() => SubtLocalPeerConfigurationBandwidthTargetString);
            }
        }
        public string SubtLocalPeerConfigurationBandwidthTargetString => SubtLocalPeerConfiguration.BandwidthTarget.BandwidthToString();
        
        public ICommand SubtLocalPeerConfigurationBandwidthTargetIncrease => new DelegateCommand(() =>
        {
            SubtLocalPeerConfigurationBandwidthTarget *= 1.2f;
        });
        public ICommand SubtLocalPeerConfigurationBandwidthTargetDecrease => new DelegateCommand(() =>
        {
            SubtLocalPeerConfigurationBandwidthTarget *= 0.8f;
        });

        #endregion

        public LocalPeer LocalPeer { get; private set; }
        public SubtLocalPeer SubtLocalPeer { get; private set; }
        public EasyGuiViewModel EasyGuiViewModel { get; private set; }
        public DowntimesTracker DowntimesTracker { get; private set; }
        static MainViewModel _instance;
        public MainViewModel()
        {
            if (_instance != null) throw new InvalidOperationException();
            _instance = this;
            _timer = new DispatcherTimer(DispatcherPriority.SystemIdle);
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick_1sec;
            _timer.Start();
            EasyGuiViewModel = new EasyGuiViewModel(this);
            DowntimesTracker = new DowntimesTracker(this);
            AutoStartedInTrayMode = Environment.GetCommandLineArgs().Contains(TrayCliParameter);
            if (AutoStartedInTrayMode)
            {
                EasyGuiViewModel.StartTest.Execute(null);
            }
        }
        public DelegateCommand Initialize => new DelegateCommand(() =>
                {
                    try
                    {
                        if (DeveloperMode == false)
                            PredefinedReleaseMode.Execute(null);

                        SubtLocalPeer = new SubtLocalPeer(SubtLocalPeerConfiguration, SubtLocalPeer);
                        LocalPeerConfiguration.Extensions = new[] { SubtLocalPeer };
                        LocalPeerConfiguration.LocalPeerUser = this;
                        LocalPeer = new LocalPeer(LocalPeerConfiguration);
                        RaisePropertyChanged(() => Initialized);
                        if (DeveloperMode)
                        {
                            ConnectedPeersTabIsSelected = true;
                            RaisePropertyChanged(() => ConnectedPeersTabIsSelected);
                        }
                        EasyGuiViewModel.OnInitialized();
                        CanHandleException = true;

                        SubtLocalPeer.MeasurementsHistory.OnMeasured += DowntimesTracker.MeasurementsHistory_OnMeasured;
                    }
                    catch (Exception exc)
                    {
                        HandleException(exc, true);
                    }
                });
        
        public bool Initialized => LocalPeer != null;
        internal static bool CanHandleException = false;
        public void Dispose()
        {
            EasyGuiViewModel.Dispose();
            if (LocalPeer != null)
            {
                LocalPeer.Dispose();
                LocalPeer = null;
            }
            _timer.Stop();
        }
        public DelegateCommand ReInitialize => new DelegateCommand(() =>
        {
            if (LocalPeer == null) throw new InvalidOperationException();
            LocalPeer.ReinitializeByGui();
        });
        public DelegateCommand DeInitialize => new DelegateCommand(() =>
        {
            if (LocalPeer != null)
            {
                LocalPeer.Dispose();
                LocalPeer = null;
            }
        });

        internal void InvokeInGuiThread(Action a)
        {
            App.Current.Dispatcher.Invoke(a);
        }

        #region refresh GUI on timer
        DispatcherTimer _timer;
        private void Timer_Tick_1sec(object sender, EventArgs e)
        {
            if (RefreshTechGuiOnTimer && TechTabIsSelected) RefreshTechGui.Execute(null);
        }
        public bool RefreshTechGuiOnTimer { get; set; } = false;
        public DelegateCommand RefreshTechGui => new DelegateCommand(() =>
        {
            try
            {
                if (Initialized)
                {
                    if (EnableLog) RaisePropertyChanged(() => LogMessages);
                    RaisePropertyChanged(() => LocalPeer);
                    RaisePropertyChanged(() => SubtLocalPeer);
                }
            }
            catch (Exception exc)
            {
                if (LocalPeer != null)
                    LocalPeer.HandleGuiException(exc);
            }
        });
        #endregion
        
        #region selected tabs
        public bool TechTabIsSelected { get; set; }
#if DEBUG2
            = true;
#else
            = false;
#endif
        public bool EasyGuiTabIsSelected { get; set; }
#if DEBUG2
            = false;
#else
            = true;
#endif
        public bool ConnectedPeersTabIsSelected { get; set; }
#endregion

#region logging
        internal static void HandleException(Exception exc, bool showMessageBox = false)
        {
            if (_instance != null && _instance.LocalPeer != null)
            {
                _instance.LocalPeer.HandleGuiException(exc);
                if (showMessageBox)
                    MessageBox.Show("Error: " + exc.Message);
            }
            else MessageBox.Show("Error: " + exc.ToString());
        }
        public int LogMessagesMaxRamCount { get; set; } = 100000;
        int _logMessagesMaxDisplayCount = 1000;
        public int LogMessagesMaxDisplayCount
        {
            get => _logMessagesMaxDisplayCount;
            set { _logMessagesMaxDisplayCount = value; RaisePropertyChanged(() => LogMessages); }
        }
        public bool EnableLog { get; set; } = true;
        string _logMessagesFilter { get; set; }
        public string LogMessagesFilter
        {
            get => _logMessagesFilter;
            set { _logMessagesFilter = value; RaisePropertyChanged(() => LogMessages); }
        }
        void ILocalPeerUser.WriteToLog(string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (EnableLog && LocalPeer != null)
                lock (_logMessages)
                {
                    _logMessages.AddLast(new LogMessage { DateTime = LocalPeer.DateTimeNowUtc, Text = message });
                    while (_logMessages.Count > LogMessagesMaxRamCount)
                        _logMessages.RemoveFirst();
                }
        }
        readonly LinkedList<LogMessage> _logMessages = new LinkedList<LogMessage>(); // from oldest to newest // locked
        public IEnumerable<LogMessage> LogMessages // from newest to oldest
        {
            get
            {
                int c = 0;
                lock (_logMessages)
                    for (var item = _logMessages.Last; item != null; item = item.Previous)
                    {
                        var msg = item.Value;
                        if (!String.IsNullOrEmpty(_logMessagesFilter))
                            if (!msg.Text.Contains(_logMessagesFilter))
                                continue;
                        yield return msg;
                        c++;
                        if (c >= _logMessagesMaxDisplayCount) break;
                    }
            }
        }
#endregion
    }
}
