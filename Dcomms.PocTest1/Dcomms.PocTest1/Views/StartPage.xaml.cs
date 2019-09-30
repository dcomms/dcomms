using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Dcomms.PocTest1.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class StartPage : ContentPage
    {
        public StartPage()
        {
            InitializeComponent();
        }

        private void NumberOfNeighbors_TextChanged(object sender, TextChangedEventArgs e)
        {
            ((Poc1Model)this.BindingContext).DrpTester3.NumberOfNeighborsToKeep = e.NewTextValue;
        }

        private void IncreaseNumberOfNeighbors_Clicked(object sender, EventArgs e)
        {

            ((Poc1Model)this.BindingContext).DrpTester3.IncreaseNumberOfNeighborsToKeep.Execute(null);
            numberOfNeighbors.Text = ((Poc1Model)this.BindingContext).DrpTester3.NumberOfNeighborsToKeep;
        }
    }
}