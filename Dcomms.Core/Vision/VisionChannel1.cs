using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace Dcomms.Vision
{
    public class VisionChannel1 : VisionChannel, INotifyPropertyChanged
    {
        public bool AttentionToRoutedPath { get; set; } = true;
        public AttentionLevel AttentionLevel { get; set; } = AttentionLevel.deepDetail;
        public AttentionLevel DisplayFilterMinLevel { get; set; } = AttentionLevel.guiActivity;
        public override AttentionLevel GetAttentionTo(string visionChannelSourceId, string moduleName)
        {
            return AttentionLevel;
        }
        public IEnumerable<AttentionLevel> DisplayFilterMinLevels => Enum.GetValues(typeof(AttentionLevel)).Cast<AttentionLevel>();
        public Action<string,List<IVisiblePeer>,VisiblePeersDisplayMode> DisplayPeersDelegate;
        public Action<object> DisplayRoutingPathDelegate;

        public string DisplayFilterSourceIds { get; set; }
        public string DisplayFilterMessageContainsString { get; set; }
        public string DisplayFilterModuleContainsStrings { get; set; }
        public string DisplayFilterModuleExcludesStrings { get; set; }
        public int DisplayedLogMessagesMaxCount { get; set; } = 30;


        public IEnumerable<LogMessage> DisplayedLogMessages
        {
            get
            {
                lock (_logMessagesNewestFirst)
                {
                    IEnumerable<LogMessage> r;
                    if (_skipNewUnselectedMessages) r = _logMessagesNewestFirst.SkipWhile(x => x.Selected == false);
                    else r = _logMessagesNewestFirst;

                    if (!String.IsNullOrEmpty(DisplayFilterSourceIds))
                    {
                        var displayFilterSourceIds = new HashSet<string>(DisplayFilterSourceIds.Split(',', ';'));
                        r = r.Where(x => displayFilterSourceIds.Contains(x.SourceId));
                    }
                    if (!String.IsNullOrEmpty(DisplayFilterMessageContainsString))
                        r = r.Where(x => x.Message.Contains(DisplayFilterMessageContainsString));
                    if (!String.IsNullOrEmpty(DisplayFilterModuleContainsStrings))
                    {
                        var modules = new HashSet<string>(DisplayFilterModuleContainsStrings.Split(',', ';'));
                        r = r.Where(x => modules.Contains(x.ModuleName));
                    }
                    if (!String.IsNullOrEmpty(DisplayFilterModuleExcludesStrings))
                    {
                        var modules = new HashSet<string>(DisplayFilterModuleExcludesStrings.Split(',', ';'));
                        r = r.Where(x => modules.Contains(x.ModuleName) == false);
                    }

                    r = r.Where(x => x.AttentionLevel >= DisplayFilterMinLevel);
                    return r.Take(DisplayedLogMessagesMaxCount).ToList();
                }
            }
        }
        public IEnumerable<LogMessage> DisplayedSelectedLogMessages
        {
            get
            {
                lock (_logMessagesNewestFirst)
                {
                    return _logMessagesNewestFirst.Where(x=>x.Selected).ToList();
                }
            }
        }
        public List<LogMessage> GetLogMessages_newestFirst(object routingPathReq)
        {          
            lock (_logMessagesNewestFirst)
            {
                IEnumerable<LogMessage> r = _logMessagesNewestFirst;
                r = r.Where(x => routingPathReq.Equals(x.RoutedPathReq));
              //  r = r.Where(x => x.AttentionLevel >= DisplayFilterMinLevel);
                return r.ToList();
            }            
        }

        public ICommand RefreshDisplayedSelectedLogMessages => new DelegateCommand(() =>
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedSelectedLogMessages"));
        });

        public LinkedList<LogMessage> _logMessagesNewestFirst = new LinkedList<LogMessage>(); // locked
        public bool EnableNewLogMessages { get; set; } = true;
        public int EnableNewLogMessagesUntilProcessRamSizeMB { get; set; } = 16000;
        
        public event PropertyChangedEventHandler PropertyChanged;

        public override void Emit(string sourceId, string moduleName, AttentionLevel level, string message)
        {
            if (!EnableNewLogMessages) return;
            var msg = new LogMessage(this)
            {
                AttentionLevel = level,
                ManagedThreadId = Thread.CurrentThread.ManagedThreadId,
                Time = TimeNow,
                SourceId = sourceId,
                ModuleName = moduleName,
                Message = message
            };
            lock (_logMessagesNewestFirst)
            {
                _logMessagesNewestFirst.AddFirst(msg);
            }

            if (_maxEmittedAttentionLevelLogMessage == null || msg.AttentionLevel >= _maxEmittedAttentionLevelLogMessage.AttentionLevel)
            {
                _maxEmittedAttentionLevelLogMessage = msg;
            }
        }
        public override void EmitListOfPeers(string sourceId, string moduleName, AttentionLevel level, string message, List<IVisiblePeer> peersList_RoutingPath, IVisiblePeer selectedPeer)
        {
            if (!EnableNewLogMessages) return;
            var msg = new LogMessage(this)
            {
                AttentionLevel = level,
                ManagedThreadId = Thread.CurrentThread.ManagedThreadId,
                Time = TimeNow,
                SourceId = sourceId,
                ModuleName = moduleName,
                Message = message,
                PeersList = peersList_RoutingPath ?? ClonedVisiblePeer.Clone(VisiblePeersDelegate(), selectedPeer),
                PeersListDisplayMode = peersList_RoutingPath != null ? VisiblePeersDisplayMode.routingPath : VisiblePeersDisplayMode.allPeers,

            };
            lock (_logMessagesNewestFirst)
            {
                _logMessagesNewestFirst.AddFirst(msg);
            }

            if (_maxEmittedAttentionLevelLogMessage == null || msg.AttentionLevel >= _maxEmittedAttentionLevelLogMessage.AttentionLevel)
            {
                _maxEmittedAttentionLevelLogMessage = msg;
            }
        }
        public override void EmitPeerInRoutedPath(string visionChannelSourceId, string moduleName, AttentionLevel level, string message, object req, IVisiblePeer localPeer)
        {
            if (!EnableNewLogMessages) return;
            var msg = new LogMessage(this)
            {
                AttentionLevel = level,
                ManagedThreadId = Thread.CurrentThread.ManagedThreadId,
                Time = TimeNow,
                SourceId = visionChannelSourceId,
                ModuleName = moduleName,
                Message = message,               

            };

            if (AttentionToRoutedPath && req != null)
            {
                msg.RoutedPathPeer = ClonedVisiblePeer.Clone(localPeer);
                msg.RoutedPathReq = req;
            }

            lock (_logMessagesNewestFirst)
            {
                _logMessagesNewestFirst.AddFirst(msg);
            }
            if (_maxEmittedAttentionLevelLogMessage == null || msg.AttentionLevel >= _maxEmittedAttentionLevelLogMessage.AttentionLevel)
            {
                _maxEmittedAttentionLevelLogMessage = msg;
            }
        }
        LogMessage _maxEmittedAttentionLevelLogMessage;
        public System.Drawing.Color RefreshDisplayedLogMessagesButtonColor
        {
            get => _maxEmittedAttentionLevelLogMessage?.AttentionLevelColor ?? System.Drawing.Color.White;
        }
        public string MaxEmittedAttentionLevelLogMessage
        {
            get => _maxEmittedAttentionLevelLogMessage?.Message;
        }
        public ICommand RefreshDisplayedLogMessages => new DelegateCommand(() =>
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedLogMessages"));
        });

        public void UpdateGui_100ms()
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("RefreshDisplayedLogMessagesButtonColor"));
                PropertyChanged(this, new PropertyChangedEventArgs("MaxEmittedAttentionLevelLogMessage"));
            }

            
            try
            {
                /*
                    sometimes it generates exception:
                    error in SIP Tester: 'WPF GUI thread' failed: System.Reflection.TargetInvocationException: 
                    Exception has been thrown by the target of an invocation. ---> 
                    System.InvalidOperationException: Couldn't get process information from performance counter. ---> 
                    System.ComponentModel.Win32Exception: Unknown error (0xc0000017)     
                    --- End of inner exception stack trace ---     
                    at System.Diagnostics.NtProcessInfoHelper.GetProcessInfos()  
                    at System.Diagnostics.ProcessManager.GetProcessInfos(String machineName)  
                    at System.Diagnostics.Process.EnsureState(State state)     
                    at System.Diagnostics.Process.get_PagedMemorySize64() 
                */
                var consumedMemoryMb = Process.GetCurrentProcess().PagedMemorySize64 / 1024 / 1024;
               
                if (consumedMemoryMb > EnableNewLogMessagesUntilProcessRamSizeMB)
                {
                    // clean 5% of oldest log messages
                    lock (_logMessagesNewestFirst)
                    {
                        int numberToDelete = _logMessagesNewestFirst.Count / 20;
                        for (int i = 0; i < numberToDelete; i++)
                            _logMessagesNewestFirst.RemoveLast();
                    }
                }
               
            }
            catch (Exception)
            {/// intentionally ignore
			}
        }


        public ICommand ClearLogMessages => new DelegateCommand(() =>
        {
            lock (_logMessagesNewestFirst)
                _logMessagesNewestFirst.Clear();
            _maxEmittedAttentionLevelLogMessage = null;

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedLogMessages"));
        });
        bool _skipNewUnselectedMessages;
        public bool SkipNewUnselectedMessages
        {
            get => _skipNewUnselectedMessages;
            set
            {
                _skipNewUnselectedMessages = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("DisplayedLogMessages"));
            }
        }

        public string VisibleModulePathContainsString { get; set; }
        public string VisibleModuleStatusContainsString { get; set; }
        public IEnumerable<VisibleModule> DisplayedVisibleModules
        {
            get
            {
                lock (_visibleModulesByPath)
                {
                    var r = _visibleModulesByPath.Select(x => new VisibleModule
                    {
                        Path = x.Key,
                        Status = x.Value.Status
                    });
                    if (!String.IsNullOrEmpty(VisibleModulePathContainsString))
                        r = r.Where(x => x.Path.Contains(VisibleModulePathContainsString));
                    if (!String.IsNullOrEmpty(VisibleModuleStatusContainsString))
                        r = r.Where(x => x.Status.Contains(VisibleModuleStatusContainsString));

                    return r.OrderBy(x => x.Path).ToList();
                }
            }
        }

        public ICommand RefreshDisplayedVisibleModules => new DelegateCommand(() =>
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedVisibleModules"));
        });

        public class VisibleModule
        {
            public string Path { get; set; }
            public string Status { get; set; }
        }

        public class LogMessage
        {
            readonly VisionChannel1 _visionChannel;
            public LogMessage(VisionChannel1 visionChannel)
            {
                _visionChannel = visionChannel;
            }

            public AttentionLevel AttentionLevel { get; set; }
            public string AttentionLevelStr => AttentionLevel.ToString();
            public System.Drawing.Color AttentionLevelColor
            {
                get
                {
                    switch (AttentionLevel)
                    {
                        case AttentionLevel.death: return System.Drawing.Color.FromArgb(255, 128, 0, 0);
                        case AttentionLevel.strongPain: return System.Drawing.Color.FromArgb(255, 200, 0, 0);
                        case AttentionLevel.mediumPain: return System.Drawing.Color.FromArgb(255, 222, 70, 0);
                        case AttentionLevel.lightPain: return System.Drawing.Color.FromArgb(255, 252, 150, 0);
                        case AttentionLevel.guiPain: return System.Drawing.Color.FromArgb(255, 252, 190, 0);
                        case AttentionLevel.needsAttention: return System.Drawing.Color.FromArgb(255, 252, 220, 0);
                        case AttentionLevel.guiActivity: return System.Drawing.Color.FromArgb(255, 190, 255, 0);
                        case AttentionLevel.higherLevelDetail: return System.Drawing.Color.FromArgb(255, 190, 255, 150);
                        case AttentionLevel.detail: return System.Drawing.Color.FromArgb(255, 210, 255, 200);
                        case AttentionLevel.deepDetail: return System.Drawing.Color.FromArgb(255, 240, 255, 235);
                        default: throw new NotImplementedException();
                    }
                }
            }
            public DateTime Time { get; set; }
            public string TimeStr => Time.ToString("dd-HH:mm:ss.fff");
            public int ManagedThreadId { get; set; }
            public string ModuleName { get; set; }
            public string SourceId { get; set; }
            public string Message { get; set; }
            public bool Selected { get; set; }
            public List<IVisiblePeer> PeersList;
            public VisiblePeersDisplayMode PeersListDisplayMode;
            public bool DisplayPeersListVisible => (PeersList != null && _visionChannel.DisplayPeersDelegate != null);
            public ICommand DisplayPeersList => new DelegateCommand(() =>
            {
                _visionChannel.DisplayPeersDelegate(Message, PeersList, PeersListDisplayMode);
            });


            public bool DisplayRoutingPathVisible => (RoutedPathReq != null && RoutedPathPeer != null && _visionChannel.DisplayRoutingPathDelegate != null);
            public ICommand DisplayRoutingPath => new DelegateCommand(() =>
            {
                _visionChannel.DisplayRoutingPathDelegate(RoutedPathReq);
            });

            public object RoutedPathReq;
            public IVisiblePeer RoutedPathPeer;
        }
        
        class ClonedVisiblePeer: IVisiblePeer
        {
            public float[] VectorValues { get; private set; }
            public bool Highlighted { get; private set; }
            public string Name { get; private set; }
            public List<IVisiblePeer> NeighborPeers { get; private set; }
            IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => NeighborPeers;            
            public static List<IVisiblePeer> Clone(List<IVisiblePeer> sourceList, IVisiblePeer forceHighlightedPeer = null)
            {
                var r = new List<ClonedVisiblePeer>(sourceList.Count);
                var sourcePeersIndexes = new Dictionary<IVisiblePeer, int>();
                for (int i = 0; i < sourceList.Count; i++)
                {
                    var sourcePeer = sourceList[i];
                    sourcePeersIndexes.Add(sourcePeer, i);
                    r.Add(new ClonedVisiblePeer
                    {
                        VectorValues = sourcePeer.VectorValues.ToArray(),
                        Highlighted = sourcePeer.Highlighted || sourcePeer == forceHighlightedPeer,
                        Name = sourcePeer.Name
                    });
                }
                
                for (int i = 0; i < sourceList.Count; i++)
                {
                    var sourcePeer = sourceList[i];
                    var clonedPeer = r[i];
                    clonedPeer.NeighborPeers = new List<IVisiblePeer>();

                    List<IVisiblePeer> sourcePeerNeighborPeers;
                    int c = 0;
_retry:
                    try
                    {
                        sourcePeerNeighborPeers = sourcePeer.NeighborPeers?.ToList();
                    }
                    catch (InvalidOperationException)
                    { // collection was modifid. retry 10 times
                        if (c++ < 10) goto _retry;
                        else throw;
                    }

                    if (sourcePeerNeighborPeers != null)
                        foreach (var neighbor in sourcePeerNeighborPeers)
                        {
                            if (sourcePeersIndexes.TryGetValue(neighbor, out var neighborIndex))
                            {                        
                                clonedPeer.NeighborPeers.Add(r[neighborIndex]);
                            }
                            else
                            {
                                clonedPeer.NeighborPeers.Add(new ClonedVisiblePeer
                                {
                                    VectorValues = neighbor.VectorValues.ToArray()
                                });
                            }
                        }
                }

                return r.Cast<IVisiblePeer>().ToList();
            }
            string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer)
            {
                return "";
            }
            
            public static IVisiblePeer Clone(IVisiblePeer sourcePeer)
            {
                var r = new ClonedVisiblePeer
                {
                    VectorValues = sourcePeer.VectorValues.ToArray(),
                    Highlighted = sourcePeer.Highlighted,
                    Name = sourcePeer.Name
                };              

                r.NeighborPeers = new List<IVisiblePeer>();
                foreach (var neighbor in sourcePeer.NeighborPeers)
                {                       
                    r.NeighborPeers.Add(new ClonedVisiblePeer
                    {
                        VectorValues = neighbor.VectorValues.ToArray()
                    });                        
                }               

                return r;
            }
            public override int GetHashCode()
            {
                return MiscProcedures.GetArrayHashCode(VectorValues);
            }
            public override bool Equals(object obj)
            {
                return MiscProcedures.EqualFloatArrays(this.VectorValues, ((ClonedVisiblePeer)obj).VectorValues);
            }
        }
    }
}
