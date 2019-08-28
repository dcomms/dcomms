using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

namespace Dcomms.CryptographyTester
{
    public partial class CryptographyTesterMainWindow : Window
    {
        public class CryptographyTesterMainWindowVisionChannel: VisionChannel
        {
            public ObservableCollection<LogMessage> LogMessages { get; private set; } = new ObservableCollection<LogMessage>();
            readonly Stopwatch _sw = Stopwatch.StartNew();
            readonly DateTime _started = DateTime.Now;
            string TimeNowStr => (_started + _sw.Elapsed).ToString("HH:mm:ss.fff");
            public override void Emit(string sourceId, string objectName, AttentionLevel level, string message)
            {
                var msg = new LogMessage
                {
                    Text = $"{TimeNowStr} [{Thread.CurrentThread.ManagedThreadId} {sourceId}] {objectName} {message}"
                };
                App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    LogMessages.Insert(0, msg);
                }));
            }
        }
        CryptographyTesterMainWindowVisionChannel VisionChannel { get; set; } = new CryptographyTesterMainWindowVisionChannel();

        readonly Dcomms.CryptographyTester1 _tester;
        public CryptographyTesterMainWindow()
        {
            _tester = new CryptographyTester1(VisionChannel);
            InitializeComponent();
            this.DataContext = _tester;
            visionGui.DataContext = VisionChannel;

        }

        public class LogMessage
        {
            public string Text { get; set; }
        }
    }
}
