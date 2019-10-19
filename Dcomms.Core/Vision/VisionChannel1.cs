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
        public AttentionLevel DisplayFilterMinLevel { get; set; } = AttentionLevel.deepDetail;
        public IEnumerable<AttentionLevel> DisplayFilterMinLevels => Enum.GetValues(typeof(AttentionLevel)).Cast<AttentionLevel>();
        public Action<string,List<IVisiblePeer>,VisiblePeersDisplayMode> DisplayPeersDelegate;

        public string DisplayFilterSourceIds { get; set; }
        public string DisplayFilterMessageContainsString { get; set; }
        public string DisplayFilterModuleContainsStrings { get; set; }
        public string DisplayFilterModuleExcludesStrings { get; set; }

        public IEnumerable<LogMessage> DisplayedLogMessages
        {
            get
            {
                lock (_logMessages)
                {
                    IEnumerable<LogMessage> r = _logMessages;
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
                    return r.ToList();
                }
            }
        }
        public IEnumerable<LogMessage> DisplayedSelectedLogMessages
        {
            get
            {
                lock (_logMessages)
                {
                    return _logMessages.Where(x=>x.Selected).ToList();
                }
            }
        }

        public ICommand RefreshDisplayedSelectedLogMessages => new DelegateCommand(() =>
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedSelectedLogMessages"));
        });

        public LinkedList<LogMessage> _logMessages = new LinkedList<LogMessage>(); // locked // from oldest to newest
        public bool EnableNewLogMessages { get; set; } = true;
        public int LogMessagesMaxCount { get; set; } = 500000;

        readonly Stopwatch _sw = Stopwatch.StartNew();
        readonly DateTime _started = DateTime.Now;

        public event PropertyChangedEventHandler PropertyChanged;
        DateTime TimeNow => _started + _sw.Elapsed;
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
            lock (_logMessages)
            {
                _logMessages.AddLast(msg);
                while (_logMessages.Count > LogMessagesMaxCount)
                    _logMessages.RemoveFirst();
            }
        }
        public override void EmitListOfPeers(string sourceId, string moduleName, AttentionLevel level, string message, List<IVisiblePeer> peersList, VisiblePeersDisplayMode peersListDisplayMode, List<IVisiblePeer> highlightedPeers)
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
                PeersList = peersListDisplayMode == VisiblePeersDisplayMode.allPeers ? ClonedVisiblePeer.Clone(peersList) : peersList,
                PeersListDisplayMode = peersListDisplayMode,

            };
            lock (_logMessages)
            {
                _logMessages.AddLast(msg);
                while (_logMessages.Count > LogMessagesMaxCount)
                    _logMessages.RemoveFirst();
            }
        }
        public ICommand RefreshDisplayedLogMessages => new DelegateCommand(() =>
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedLogMessages"));
        });
        public ICommand ClearLogMessages => new DelegateCommand(() =>
        {
            lock (_logMessages)
                _logMessages.Clear();

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedLogMessages"));
        });

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
            public string TimeStr => Time.ToString("HH:mm:ss.fff");
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
        }


        class ClonedVisiblePeer: IVisiblePeer
        {
            public float[] VectorValues { get; private set; }
            public bool Highlighted { get; private set; }
            public List<IVisiblePeer> NeighborPeers { get; private set; }
            IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => NeighborPeers;
            
            public static List<IVisiblePeer> Clone(List<IVisiblePeer> sourceList)
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
                        Highlighted = sourcePeer.Highlighted
                    });
                }
                
                for (int i = 0; i < sourceList.Count; i++)
                {
                    var sourcePeer = sourceList[i];
                    var clonedPeer = r[i];
                    clonedPeer.NeighborPeers = new List<IVisiblePeer>();
                    foreach (var neighbor in sourcePeer.NeighborPeers)
                    {
                        var neighborIndex = sourcePeersIndexes[neighbor];
                        clonedPeer.NeighborPeers.Add(r[neighborIndex]);
                    }
                }

                return r.Cast<IVisiblePeer>().ToList();
            }

            string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer)
            {
                throw new NotImplementedException();
            }
        }
    }
}
