using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]
namespace StarTrinity.CST
{
    public partial class App : Application
    {
        public App(IXamarinMainPageHost host)
        {
            InitializeComponent();
            Dcomms.MiscProcedures.Initialize(CompilationInfo.CompilationDateTimeUtc);
            MainPage = new XamarinMainPage(host);            
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
