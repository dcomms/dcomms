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

        public AttentionLevel DisplayFilterMinLevel { get; set; }
        public IEnumerable<AttentionLevel> DisplayFilterMinLevels => Enum.GetValues(typeof(AttentionLevel)).Cast<AttentionLevel>();


        public string DisplayFilterSourceId { get; set; }
        public string DisplayFilterMessageContainsString { get; set; }
        public string DisplayFilterModuleContainsStrings { get; set; }

        public IEnumerable<LogMessage> DisplayedLogMessages
        {
            get
            {
                lock (_logMessages)
                {
                    IEnumerable<LogMessage> r = _logMessages;
                    if (!String.IsNullOrEmpty(DisplayFilterSourceId))
                        r = r.Where(x => x.SourceId == DisplayFilterSourceId);
                    if (!String.IsNullOrEmpty(DisplayFilterMessageContainsString))
                        r = r.Where(x => x.Message.Contains(DisplayFilterMessageContainsString));
                    if (!String.IsNullOrEmpty(DisplayFilterModuleContainsStrings))
                    {
                        var modules = DisplayFilterModuleContainsStrings.Split(',', ';');
                        r = r.Where(x => modules.Any(module => x.ModuleName.Contains(module)));
                    }


                    r = r.Where(x => x.AttentionLevel >= DisplayFilterMinLevel);
                    return r.ToList();
                }
            }
        }

        public LinkedList<LogMessage> _logMessages = new LinkedList<LogMessage>(); // locked // from oldest to newest
        readonly Stopwatch _sw = Stopwatch.StartNew();
        readonly DateTime _started = DateTime.Now;

        public event PropertyChangedEventHandler PropertyChanged;
        DateTime TimeNow => _started + _sw.Elapsed;
        public override void Emit(string sourceId, string moduleName, AttentionLevel level, string message)
        {
            var msg = new LogMessage
            {
                AttentionLevel = level,
                ManagedThreadId = Thread.CurrentThread.ManagedThreadId,
                Time = TimeNow,
                SourceId = sourceId,
                ModuleName = moduleName,
                Message = message
            };
            lock (_logMessages)
                _logMessages.AddLast(msg);
        }
        public ICommand RefreshGui => new DelegateCommand(() =>
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("DisplayedLogMessages"));
        });

        public class LogMessage
        {
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
                        case AttentionLevel.lightPain: return System.Drawing.Color.FromArgb(255, 222, 120, 0);
                        case AttentionLevel.guiPain: return System.Drawing.Color.FromArgb(255, 222, 180, 0);
                        case AttentionLevel.needsAttention: return System.Drawing.Color.FromArgb(255, 222, 255, 0);
                        case AttentionLevel.guiActivity: return System.Drawing.Color.FromArgb(255, 180, 255, 0);
                        case AttentionLevel.detail: return System.Drawing.Color.FromArgb(255, 180, 220, 100);
                        case AttentionLevel.deepDetail: return System.Drawing.Color.FromArgb(255, 180, 220, 200);
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
        }
    }
}
