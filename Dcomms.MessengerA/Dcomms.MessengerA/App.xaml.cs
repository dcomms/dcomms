using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Dcomms.MessengerA.Services;
using Dcomms.MessengerA.Views;

namespace Dcomms.MessengerA
{
    public partial class App : Application
    {

        public App(string databaseBasePath)
        {
            InitializeComponent();

            DependencyService.Register<MockDataStore>();
            MainPage = new MainPage();
        }

        protected override void OnStart()
        {
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}
