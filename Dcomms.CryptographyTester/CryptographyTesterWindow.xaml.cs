using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

namespace Dcomms.CryptographyTester
{
    public partial class CryptographyTesterMainWindow : Window
    {
        public class CryptographyTesterMainWindowVisionChannel: VisionChannel
        {
            public ObservableCollection<LogMessage> LogMessages { get; private set; } = new ObservableCollection<LogMessage>();

            public override void Emit(string objectName, string sourceCodePlaceId, AttentionLevel level, string message)
            {
                LogMessages.Add(new LogMessage { Text = $"{objectName} {sourceCodePlaceId} {message}" });
            }
        }
        CryptographyTesterMainWindowVisionChannel VisionChannel { get; set; } = new CryptographyTesterMainWindowVisionChannel();

        readonly CCP.CryptographyTester _tester;
        public CryptographyTesterMainWindow()
        {
            _tester = new CCP.CryptographyTester(VisionChannel);
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
