using Dcomms.SUBT.GUI;
using System;
using System.Collections.Generic;
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

namespace StarTrinity.ContinuousSpeedTest
{
    public partial class ConnectedPeersView : UserControl
    {
        public ConnectedPeersView()
        {
            InitializeComponent();
        }

        private void CopyRemoteEndpoints_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            var minDate = new DateTime(2020, 3, 3);
            foreach (var cp in ((CstApp)DataContext).SubtLocalPeer.ConnectedPeers)
                if (cp.RemoteLibraryVersion > minDate)
                {
                    foreach (var s in cp.Streams)
                    {
                        sb.Append(s.Stream.RemoteEndPointString);
                        sb.Append(";");
                    }
                    sb.Append("\r\n");
                }


            Clipboard.SetText(sb.ToString());
            MessageBox.Show("copied to clipboard");
        }
    }
}
