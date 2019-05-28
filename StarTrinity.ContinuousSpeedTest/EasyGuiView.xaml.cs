using Dcomms.SUBT;
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
    public partial class EasyGuiView : UserControl
    {
        EasyGuiViewModel _vm;
        public EasyGuiView()
        {
            InitializeComponent();
            DataContextChanged += EasyGuiView_DataContextChanged;
        }

        private void GotoMeasurementButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var f = (UpDownTimeFragment)button.DataContext;
            _vm.MeasurementsTabIsSelected = true;
            _vm.GoToMeasurement(f.StopTime);
        }

        private void EasyGuiView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var vm = e.NewValue as EasyGuiViewModel;
            if (vm != null)
            {
                _vm = vm;
             //   vm.OnAddedNewMeasurement += EasyGuiView_OnAddedNewMeasurement;
            }
        }

        //private void EasyGuiView_OnAddedNewMeasurement(SubtMeasurement m)
        //{
        //    Dispatcher.BeginInvoke(new Action(() =>
        //    {
        //        if (VisualTreeHelper.GetChildrenCount(measurementsDataGrid) != 0)
        //        {
        //            var border = VisualTreeHelper.GetChild(measurementsDataGrid, 0) as Decorator;
        //            if (border != null)
        //            {
        //                var scroll = border.Child as ScrollViewer;
        //                if (scroll != null) scroll.ScrollToEnd();
        //            }
        //        }


        //       // measurementsDataGrid.ScrollIntoView(m);
        //    }));

        // }
    }
}
