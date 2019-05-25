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
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        readonly MainViewModel _mainVM = new MainViewModel();
        bool _exitMenuItemClicked;

        public MainWindow()
        {
            this.DataContext = _mainVM;
            InitializeComponent();
            this.Title += " version " + CompilationInfo.CompilationDateTimeUtcStr;

            if (_mainVM.AutoStartedInTrayMode)
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                _notifyIcon.Text = this.Title;
                var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/StarTrinity.ContinuousSpeedTest;component/icon.ico")).Stream;
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream);

                
                var contextMenu = new System.Windows.Forms.ContextMenu();
                var menuItem1 = new System.Windows.Forms.MenuItem
                {
                    Index = 0,
                    Text = "E&xit"
                };
                menuItem1.Click += (s, e) => { _exitMenuItemClicked = true; this.Close(); };
                contextMenu.MenuItems.AddRange(new [] { menuItem1 });


                _notifyIcon.ContextMenu = contextMenu;
                _notifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseDoubleClick);
                this.WindowState = WindowState.Minimized;
                this.Hide();
                _notifyIcon.Visible = true;
                this.ShowInTaskbar = false;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _mainVM.Dispose();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }

        void NotifyIcon_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            this.Show();
            this.Activate();
            this.ShowInTaskbar = true;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (_mainVM.AutoStartedInTrayMode)
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.ShowInTaskbar = false;
                    _notifyIcon.BalloonTipTitle = this.Title;
                    _notifyIcon.BalloonTipText = "running in background";
                    _notifyIcon.ShowBalloonTip(400);
                }
                else
                {
                    this.ShowInTaskbar = true;
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_mainVM.AutoStartedInTrayMode && _exitMenuItemClicked == false)
            {
                e.Cancel = true;
                this.WindowState = WindowState.Minimized;
            }
        }
    }
}
