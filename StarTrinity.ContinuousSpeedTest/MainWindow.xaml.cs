using Dcomms.P2PTP;
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
    public partial class MainWindow : Window
    {
        readonly MainViewModel _mainVM = new MainViewModel();
        public MainWindow()
        {
         //   MessageBox.Show("mainwindow 01");
            this.DataContext = _mainVM;
            InitializeComponent();
            this.Title += " version " + CompilationInfo.CompilationDateTimeUtcStr;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _mainVM.Dispose();
        }
    }
}
