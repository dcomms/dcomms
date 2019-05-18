using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace StarTrinity.CST
{
    public partial class MainPage : ContentPage
    {
        MainViewModel _mainVM;
        public MainPage()
        {
            InitializeComponent();
            _mainVM = new MainViewModel();
            this.BindingContext = _mainVM;
        }
    }
}
