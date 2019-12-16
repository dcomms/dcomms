using System;

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.IO;
using Android.Content;

namespace StarTrinity.CST.Droid
{
    [Activity(Label = "StarTrinity.CST", Icon = "@mipmap/icon", Theme = "@style/MainTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity, IXamarinMainPageHost
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            base.OnCreate(savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            LoadApplication(new App(this));
        }

        bool IXamarinMainPageHost.ShowSaveFileDialog(string fileExtension, out string fileName, out Action optionalFileWrittenCallback)
        {
          //  var tmpPath = Path.GetTempPath();
            var tmpPath = CacheDir.AbsolutePath;
            var fn = Path.Combine(tmpPath, $"CST_export_{DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")}.{fileExtension}");
            fileName = fn;

            optionalFileWrittenCallback = () =>
            {
                var contentType = "text/csv";               

                var shareIntent = new Intent(Intent.ActionSend);
                shareIntent.SetType(contentType);


                var file = new Java.IO.File(fn);
                var fileUri = Android.Support.V4.Content.FileProvider.GetUriForFile(ApplicationContext, "com.StarTrinity.CST.provider", file);

                
             //   shareIntent.PutExtra(Intent.ExtraSubject, $"{fileExtension} export");
                shareIntent.PutExtra(Intent.ExtraStream, fileUri);
                shareIntent.SetDataAndType(fileUri, Application.Context.ContentResolver.GetType(fileUri));

                shareIntent.SetFlags(ActivityFlags.GrantReadUriPermission);
                shareIntent.SetFlags(ActivityFlags.NewTask);
                //StartActivity(shareIntent);

                var chooserIntent = Intent.CreateChooser(shareIntent, "export");
                chooserIntent.SetFlags(ActivityFlags.ClearTop);
                chooserIntent.SetFlags(ActivityFlags.NewTask);
                StartActivity(shareIntent);

                //res
                //   var chooserIntent = Intent.CreateChooser(shareIntent, string.Empty);
                //   chooserIntent.SetFlags(ActivityFlags.ClearTop);
                //   chooserIntent.SetFlags(ActivityFlags.NewTask);
                //   StartActivity(chooserIntent);
            };

            return true;

            //  throw new NotImplementedException();
            //IFileSystem fileSystem = FileSystem.Current;

            //var filePicker = App.PresentationFactory.CreateFilePicker();

            //await filePicker.PickAndOpenFileForWriting(fileTypes, defaultFileName)
        }
    }
}