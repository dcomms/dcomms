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

        public string DisplayFilterSourceId { get; set; }
        public string DisplayFilterMessageContainsString { get; set; }

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
            public DateTime Time { get; set; }
            public string TimeStr => Time.ToString("HH:mm:ss.fff");
            public int ManagedThreadId { get; set; }
            public string ModuleName { get; set; }
            public string SourceId { get; set; }
            public string Message { get; set; }
        }
    }
}
