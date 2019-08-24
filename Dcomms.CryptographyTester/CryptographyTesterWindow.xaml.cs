using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            [DllImport("kernel32.dll")]
            static extern uint GetCurrentThreadId();

            public override void Emit(string objectName, string sourceCodePlaceId, AttentionLevel level, string message)
            {
                var msg = new LogMessage
                {
                    Text = $"[{Thread.CurrentThread.ManagedThreadId}] {objectName} {sourceCodePlaceId} {message}"
                };
                App.Current.Dispatcher.BeginInvoke(new Action(() =>
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
