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
        VisionChannel1 VisionChannel { get; set; } = new VisionChannel1();

        readonly Dcomms.CryptographyTester1 _tester; 
        public CryptographyTesterMainWindow()
        {
            _tester = new CryptographyTester1(VisionChannel);
            InitializeComponent();
            this.DataContext = _tester;
            visionGui.DataContext = VisionChannel;

            this.Closed += CryptographyTesterMainWindow_Closed;
        }

        private void CryptographyTesterMainWindow_Closed(object sender, EventArgs e)
        {
            _tester.Dispose();
        }
    }
}
