using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace StarTrinity.ContinuousSpeedTest.Xamarin
{
    public partial class MainPage : ContentPage
    {
        MainViewModel _mainVM;
        public MainPage()
        {
            _mainVM = new MainViewModel();
            InitializeComponent();
            this.BindingContext = _mainVM;
        }
    }
}
