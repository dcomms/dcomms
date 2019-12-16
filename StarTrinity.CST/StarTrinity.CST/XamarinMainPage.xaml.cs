using Dcomms.SUBT.GUI;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace StarTrinity.CST
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class XamarinMainPage : TabbedPage, ICstAppUser
    {
        CstApp _cstApp;
        IXamarinMainPageHost _host;
        public XamarinMainPage(IXamarinMainPageHost host)
        {
            _host = host;
            InitializeComponent();
            _cstApp = new CstApp(this);
            this.BindingContext = _cstApp;

#if DEBUG// || true
            cryptographyTesterPage.BindingContext = new Dcomms.Sandbox.SandboxTester1(
                new SimplestVisionChannel((msg)=>
                    {
                        cryptographyTesterOutput.Text = msg;
                    })
               );
#else
            this.Children.Remove(cryptographyTesterPage);
#endif
        }
        bool ICstAppUser.RunningInstalledOnThisPC => true;
        void ICstAppUser.AddStaticResource(string name, object value)
        {
            Application.Current.Resources.Add(name, value);
        }
        void ICstAppUser.CreateIdleGuiTimer(TimeSpan interval, Action cb)
        {
            Device.StartTimer(interval, () =>
            {
                cb();
                return true; // True = Repeat again, False = Stop the timer
            });
        }
        void ICstAppUser.InstallOnThisPC()
        {
        }
        void ICstAppUser.ShowMessageToUser(string msg)
        {
            DisplayAlert("Continuous Speed Test", msg, "OK");
        }
        bool ICstAppUser.ShowSaveFileDialog(string fileExtension, out string fileName, out Action optionalFileWrittenCallback)
        {
            return _host.ShowSaveFileDialog(fileExtension, out fileName, out optionalFileWrittenCallback);
        }
        void ICstAppUser.UninstallOnThisPC()
        {
        }

        private void GotoMeasurement_Clicked(object sender, EventArgs e)
        {
            var button = (Button)sender;
            var f = (UpDownTimeFragment)button.BindingContext;
            if (this.SelectedItem == measurementsContentPage)
            { // a bug in xamarin, workaround
                this.SelectedItem = uptimeContentPage;
            }
            this.SelectedItem = measurementsContentPage;
            _cstApp.EasyGuiViewModel.GoToMeasurement(f.StopTime);

        }

        public void ConfigureFirewallIfNotConfigured()
        {
        }

        string ICstAppUser.CsvDelimiter => ",";
        CultureInfo ICstAppUser.CsvCultureInfo => new System.Globalization.CultureInfo("en-US");
    }


    public interface IXamarinMainPageHost
    {
        bool ShowSaveFileDialog(string fileExtension, out string fileName, out Action optionalFileWrittenCallback);
    }
}