using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]
namespace StarTrinity.CST
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Dcomms.MiscProcedures.Initialize(CompilationInfo.CompilationDateTimeUtc, new DateTime(2019, 05, 01));

            MainPage = new XamarinMainPage();

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
