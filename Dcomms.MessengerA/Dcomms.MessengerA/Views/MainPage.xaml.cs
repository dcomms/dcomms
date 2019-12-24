using Dcomms.UserApp;
using Dcomms.Vision;
using System;
using System.ComponentModel;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Dcomms.MessengerA.Views
{
    // Learn more about making custom code visible in the Xamarin.Forms previewer
    // by visiting https://aka.ms/xamarinforms-previewer
    [DesignTimeVisible(false)]
    public partial class MainPage : TabbedPage
    {
        public MainPage(UserAppEngine userAppEngine, VisionChannel1 visionChannel)
        {
            InitializeComponent();
            this.BindingContext = userAppEngine;
            VisionChannelPage.BindingContext = visionChannel;
        }

    }
}