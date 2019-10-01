using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Dcomms.Sandbox
{
    public class DrpDistanceTester : BaseNotify, IDisposable
    {
        public int? NumberOfPeers { get; set; } = 1000;
        public int? NumberOfNeighbors { get; set; } = 10;
        

        readonly VisionChannel _visionChannel;
        public DrpDistanceTester(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }



        public ICommand Test => new DelegateCommand(() =>
        {
          
          
            for (int i = 0; i < NumberOfPeers; i++)
            {

            }

        });

        public void Dispose()
        {
        }
    }
}
