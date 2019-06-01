using Dcomms.SUBT.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace StarTrinity.CST
{
    public partial class MainPage : ContentPage, ICstAppUser
    {
        CstApp _cstApp;
        public MainPage()
        {
            InitializeComponent();
            _cstApp = new CstApp(this);
            this.BindingContext = _cstApp;
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
            DisplayAlert("CST", msg, "OK");
        }
        bool ICstAppUser.ShowSaveFileDialog(string fileExtension, out string fileName)
        {
            throw new NotImplementedException();
            //IFileSystem fileSystem = FileSystem.Current;

            //var filePicker = App.PresentationFactory.CreateFilePicker();

            //await filePicker.PickAndOpenFileForWriting(fileTypes, defaultFileName)
        }
        void ICstAppUser.UninstallOnThisPC()
        {
        }
    }
}
