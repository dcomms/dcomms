using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Dcomms.MessengerA.Services;
using Dcomms.MessengerA.Views;
using Dcomms.UserApp;
using Dcomms.Vision;

namespace Dcomms.MessengerA
{
    public partial class App : Application
    {

        public App(UserAppEngine userAppEngine, VisionChannel1 visionChannel)
        {
            InitializeComponent();
            DependencyService.Register<MockDataStore>();
            MainPage = new MainPage(userAppEngine, visionChannel);
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
