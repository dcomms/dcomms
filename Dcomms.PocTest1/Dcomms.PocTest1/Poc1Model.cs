using Dcomms.Sandbox;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.PocTest1
{
    public class Poc1Model : IDisposable
    {
        public VisionChannel1 VisionChannel { get; set; } = new VisionChannel1();
        public DrpTester5 DrpTester5 { get; set; }
        public Poc1Model()
        {
            DrpTester5 = new DrpTester5(VisionChannel);
        }
        public void Dispose()
        {
            DrpTester5.Dispose();
        }
    }
}
