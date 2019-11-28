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
        public Poc1Model(bool drpTester5_InitializeUser1EchoResponder = false)
        {
            DrpTester5 = new DrpTester5(VisionChannel);
            if (drpTester5_InitializeUser1EchoResponder)
            {
                DrpTester5.InitializeUser1EchoResponder.Execute(null);
            }
        }
        public void Dispose()
        {
            DrpTester5.Dispose();
        }
    }
}
