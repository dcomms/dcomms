using Dcomms.DRP;
using Dcomms.Vision;
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
using System.Windows.Shapes;

namespace Dcomms.SandboxTester
{

    public partial class PeersDisplayWindow : Window
    {
        readonly List<IVisiblePeer> _peers;
        readonly VisiblePeersDisplayMode _displayMode;
        public PeersDisplayWindow(string text, List<IVisiblePeer> peers, VisiblePeersDisplayMode displayMode)
        {
            _peers = peers;
            _displayMode = displayMode;
            InitializeComponent();
            this.Title = text;

            this.Initialized += PeersDisplayWindow_Initialized;
            this.SizeChanged += PeersDisplayWindow_SizeChanged;

            if (displayMode == VisiblePeersDisplayMode.routingPath)
            {
                text1.Visibility = Visibility.Visible;
                var sb = new StringBuilder();
                sb.Append("distances to target: ");
                for (int i = 0; i < peers.Count; i++)
                    sb.Append($"hop{i}({peers[i].Name}):{peers[peers.Count-1].GetDistanceString(peers[i])};  ");
               
                text1.Text = sb.ToString();
            }
        }

        private void PeersDisplayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Display();
        }

        private void PeersDisplayWindow_Initialized(object sender, EventArgs e)
        {
            Display();
        }

        void Display()
        {
            canvas.Children.Clear();

            if (_displayMode == VisiblePeersDisplayMode.routingPath)
                foreach (var peer in _peers)
                    foreach (var neighborPeer in peer.NeighborPeers)
                        foreach (var neighborPeer2 in neighborPeer.NeighborPeers)
                        {
                            DisplayConnection(Colors.LightGreen, peer, neighborPeer2, 1);
                            DisplayPeer(Colors.LightGreen, neighborPeer2, 2);
                        }
                              

            foreach (var peer in _peers)
                foreach (var neighborPeer in peer.NeighborPeers)
                {                  
                    DisplayConnection(Colors.Green, peer, neighborPeer, 1);
                    if (_displayMode == VisiblePeersDisplayMode.routingPath) DisplayPeer(Colors.Green, neighborPeer, 3);
                }
          
            for (int i = 0; i < _peers.Count; i++)
            {
                var peer = _peers[i];
                DisplayPeer(
                    peer.Highlighted ? Colors.Red : Color.FromRgb(100, 50, (byte)(i * 255 / _peers.Count)),
                    peer, 4, peer.Name);
            }

        }

        const double margin = 20;
        Point GetPosition(IVisiblePeer peer)
        {
            var v = peer.VectorValues;
            var x = v[0];
            var y = v[1];         
            return new Point(margin+ x * (canvas.ActualWidth- margin * 2), margin + y * (canvas.ActualHeight - margin * 2));
        }
        
        void GetPosition(IVisiblePeer peer1, IVisiblePeer peer2, out Point p1, out Point p2)
        {
            var v1 = peer1.VectorValues;
            var x1 = v1[0];
            var y1 = v1[1];
            
            var v2 = peer2.VectorValues;
            var x2 = v2[0];
            var y2 = v2[1];

            RegistrationIdDistance.ProcessVectorInLoopedRegistrationIdSpace(x1, ref x2);
            RegistrationIdDistance.ProcessVectorInLoopedRegistrationIdSpace(y1, ref y2);

            p1 = new Point(margin + x1 * (canvas.ActualWidth - margin * 2), margin + y1 * (canvas.ActualHeight - margin * 2));
            p2 = new Point(margin + x2 * (canvas.ActualWidth - margin * 2), margin + y2 * (canvas.ActualHeight - margin * 2));
        }

        void DisplayPeer(Color color, IVisiblePeer peer, double radius, string text=null)
        {
            var p = GetPosition(peer);
            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(color)
            };

            Canvas.SetTop(ellipse, p.Y - radius);
            Canvas.SetLeft(ellipse, p.X - radius);
            canvas.Children.Add(ellipse);

            if (text != null)
            {
                var textBlock = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Colors.Black),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12                    
                };
                
                Canvas.SetLeft(textBlock, p.X);
                Canvas.SetTop(textBlock, p.Y);
                canvas.Children.Add(textBlock);
            }

        }
        void DisplayConnection(Color color, IVisiblePeer peer1, IVisiblePeer peer2, double thickness)
        {
            GetPosition(peer1, peer2, out var p1, out var p2);

            var line = new Line
            {
                Stroke = new SolidColorBrush(color),
                X1 = p1.X,
                X2 = p2.X,
                Y1 = p1.Y,
                Y2 = p2.Y,
                StrokeThickness = thickness
            };
            canvas.Children.Add(line);
        }
    }
}
