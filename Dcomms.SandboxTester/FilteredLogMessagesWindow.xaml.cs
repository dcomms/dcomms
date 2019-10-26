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
using static Dcomms.Vision.VisionChannel1;

namespace Dcomms.SandboxTester
{
    /// <summary>
    /// Interaction logic for FilteredLogMessagesWindow.xaml
    /// </summary>
    public partial class FilteredLogMessagesWindow : Window
    {
        public FilteredLogMessagesWindow(List<LogMessage> logMessages)
        {
            InitializeComponent();
            logMessagesDataGrid.ItemsSource = logMessages;
        }
    }
}
