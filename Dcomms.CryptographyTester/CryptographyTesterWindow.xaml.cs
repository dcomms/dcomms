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

namespace Dcomms.CryptographyTester
{
    public partial class CryptographyTesterMainWindow : Window
    {
        readonly CCP.CryptographyTester _tester;
        public CryptographyTesterMainWindow()
        {
            _tester = new CCP.CryptographyTester((msg) => MessageBox.Show(msg));
            InitializeComponent();
            this.DataContext = _tester;
        }
    }
}
