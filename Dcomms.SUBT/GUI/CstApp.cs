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
using System.IO;

namespace Dcomms.SUBT.GUI
{
    public class CstApp : BaseNotify, IDisposable, ILocalPeerUser
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

        #region installation, run mode
        public bool AutoStartedInTrayMode { get; set; }
        public const string TrayCliParameter = "/tray";
        public bool RunningInstalledOnThisPC => User.RunningInstalledOnThisPC;
        public ICommand InstallOnThisPC => new DelegateCommand(User.InstallOnThisPC);     
        public ICommand UninstallOnThisPc => new DelegateCommand(User.UninstallOnThisPC);
        public bool InstallOnThisPC_AddToAutoStart { get; set; } = true;
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
        static CstApp _instance;
        internal ICstAppUser User { get; private set; }
        public CstApp(ICstAppUser user)
        {
            if (_instance != null) throw new InvalidOperationException();
            _instance = this;
            User = user;
            user.CreateIdleGuiTimer(TimeSpan.FromSeconds(1), Timer_Tick_1sec);
            user.CreateIdleGuiTimer(TimeSpan.FromMilliseconds(50), Timer_Tick_50ms);
            
            EasyGuiViewModel = new EasyGuiViewModel(this);
            DowntimesTracker = new DowntimesTracker(this);
            AutoStartedInTrayMode = Environment.GetCommandLineArgs().Contains(TrayCliParameter);
            if (AutoStartedInTrayMode)
            {
                EasyGuiViewModel.StartTest.Execute(null);
            }


         //   if (AutoStartedInTrayMode || User.RunningInstalledOnThisPC)
                EasyGuiTabIsSelected = true;
     //       else
        //        HowItWorksTabIsSelected = true;

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
        public static bool CanHandleException = false;
        public void Dispose()
        {
            EasyGuiViewModel.Dispose();
            if (LocalPeer != null)
            {
                LocalPeer.Dispose();
                LocalPeer = null;
            }
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

        ActionsQueue _guiThreadQueue = new ActionsQueue(e => CstApp.HandleException(e));
        internal void BeginInvokeInGuiThread(Action a)
        {
            _guiThreadQueue.Enqueue(a);
        }
        private void Timer_Tick_50ms()
        {
            _guiThreadQueue.ExecuteQueued();
        }

        #region refresh GUI on timer
        private void Timer_Tick_1sec()
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
        public bool EasyGuiTabIsSelected { get; set; }
        public bool HowItWorksTabIsSelected { get; set; }
        public bool ConnectedPeersTabIsSelected { get; set; }
        #endregion

        #region logging
        public static void HandleException(Exception exc, bool showMessageBox = false)
        {
            if (_instance != null && _instance.LocalPeer != null)
            {
                _instance.LocalPeer.HandleGuiException(exc);
                if (showMessageBox)
                    _instance?.User.ShowMessageToUser("Error: " + exc.Message);
            }
            else _instance?.User.ShowMessageToUser("Error: " + exc.ToString());
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
