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
        public PeersDisplayWindow(string text, List<IVisiblePeer> peers)
        {
            _peers = peers;
            InitializeComponent();
            this.Title = text;

            this.Initialized += PeersDisplayWindow_Initialized;
            this.SizeChanged += PeersDisplayWindow_SizeChanged;
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



            foreach (var peer in _peers)
            {
                foreach (var neighborPeer in peer.NeighborPeers)
                {
                    foreach (var neighborPeer2 in neighborPeer.NeighborPeers)
                    {
                        DisplayConnection(Colors.LightGreen, peer, neighborPeer2, 1);
                        DisplayPeer(Colors.LightGreen, neighborPeer2, 3);
                    }
                }
            }

            foreach (var peer in _peers)
            {
                foreach (var neighborPeer in peer.NeighborPeers)
                {                  
                    DisplayConnection(Colors.Green, peer, neighborPeer, 2);
                    DisplayPeer(Colors.Green, neighborPeer, 8);
                }
            }

             for (int i = 0; i < _peers.Count; i++)
            {
                var peer = _peers[i];  

                DisplayPeer(Color.FromRgb(255, 50, (byte)(i * 255 / _peers.Count)), peer, 15, i.ToString());
            }

        //    for (int i = 0; i < _peers.Count-1; i++)
        //    {
       //         DisplayConnection(Colors.Brown, _peers[i], _peers[i + 1], 2);
       //     }
        }

        Point GetPosition(IVisiblePeer peer)
        {
            var v = peer.VectorValues;
            var x = v[0];
            var y = v[1];
            return new Point(x / UInt32.MaxValue * canvas.ActualWidth, y / UInt32.MaxValue * canvas.ActualHeight);
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
                    FontSize = 20
                };
                
                Canvas.SetLeft(textBlock, p.X - 20);
                Canvas.SetTop(textBlock, p.Y - 10);
                canvas.Children.Add(textBlock);
            }

        }
        void DisplayConnection(Color color, IVisiblePeer peer1, IVisiblePeer peer2, double thickness)
        {
            var p1 = GetPosition(peer1);
            var p2 = GetPosition(peer2);

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
