using System;
using System.Collections.Generic;
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
        bool ShowSaveFileDialog(string fileExtension, out string fileName);
        bool RunningInstalledOnThisPC { get; }
    }
}
