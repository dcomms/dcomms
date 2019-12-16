using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dcomms.SUBT.GUI
{
    public interface ICstAppUser
    {
        void CreateIdleGuiTimer(TimeSpan interval, Action cb);
        void AddStaticResource(string name, object value);
        void InstallOnThisPC();
        void UninstallOnThisPC();
        void ShowMessageToUser(string msg);
        /// <param name="fileName">can be a temp. file name, which will be shared after writing</param>
        /// <param name="optionalFileWrittenCallback">is invoked by caller,  android-side implementation shares written file when it is written</param>
        /// <returns></returns>
        bool ShowSaveFileDialog(string fileExtension, out string fileName, out Action optionalFileWrittenCallback);
        bool RunningInstalledOnThisPC { get; }
        string CsvDelimiter { get; }
        CultureInfo CsvCultureInfo { get; }
        void ConfigureFirewallIfNotConfigured();
    }
}
