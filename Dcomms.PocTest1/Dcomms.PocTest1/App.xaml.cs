using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Dcomms.PocTest1.Views;
using Dcomms.Sandbox;
using Dcomms.Vision;

namespace Dcomms.PocTest1
{
    public partial class App : Application
    {
        Poc1Model _poc1Model = new Poc1Model();
        public App()
        {
            InitializeComponent();
                        
            MainPage = new MainPage();
            MainPage.BindingContext = _poc1Model;
        }

        protected override void OnStart()
        {
            // Handle when your app starts
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }
    }
}
