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

namespace StarTrinity.ContinuousSpeedTest
{
    public class MainViewModel : BaseNotify, IDisposable, ILocalPeerUser
    {
        internal const float InitialBandwidthTarget = 30000;

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
        }
        public DelegateCommand Initialize => new DelegateCommand(() =>
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
#if DEBUG
            = true;
#else
            = false;
#endif
        public bool EasyGuiTabIsSelected { get; set; }
#if DEBUG
            = false;
#else
            = true;
#endif
        public bool ConnectedPeersTabIsSelected { get; set; }
#endregion

#region logging
        internal static void HandleException(Exception exc)
        {
            if (_instance != null && _instance.LocalPeer != null)
            {
                _instance.LocalPeer.HandleGuiException(exc);
            }
            else System.Windows.MessageBox.Show("Error: " + exc.ToString());
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
