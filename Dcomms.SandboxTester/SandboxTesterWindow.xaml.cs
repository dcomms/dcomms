using Dcomms.Sandbox;
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

namespace Dcomms.SandboxTester
{
    public partial class SandboxTesterMainWindow : Window
    {
        VisionChannel1 VisionChannel { get; set; } = new VisionChannel1(true);

        readonly SandboxTester1 _tester;

        Timer _refreshVisionChannelUiTimer;
        public SandboxTesterMainWindow()
        {
            _tester = new SandboxTester1(VisionChannel);
            InitializeComponent();
            this.DataContext = _tester;
            visionGui.DataContext = VisionChannel;

            this.Closed += CryptographyTesterMainWindow_Closed;

            _refreshVisionChannelUiTimer = new Timer((o) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    VisionChannel.UpdateGui_100ms();
                });
            }, null, 0, 100);

          

            VisionChannel.DisplayPeersDelegate = (text, peersList, mode) =>
            {
                var wnd = new PeersDisplayWindow(text, peersList, mode);
                wnd.Show();
            };
            VisionChannel.DisplayRoutingPathDelegate = (req) =>
            {
                var logMessages_newestFirst = VisionChannel.GetLogMessages_newestFirst(req);

                var peers = logMessages_newestFirst.Select(x => x.RoutedPathPeer).ToList();                
                peers.Reverse();
                var peersWnd = new PeersDisplayWindow($"routing for {req}", peers.Distinct().ToList(), VisiblePeersDisplayMode.routingPath);
                peersWnd.Show();
                
                var logWnd = new FilteredLogMessagesWindow(logMessages_newestFirst) { Title = $"routing for {req}" };
                logWnd.Show();

            };
        }
        
        private void CryptographyTesterMainWindow_Closed(object sender, EventArgs e)
        {
            _refreshVisionChannelUiTimer.Dispose();
            _tester.Dispose();
        }
    }
}
