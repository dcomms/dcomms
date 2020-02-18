﻿using Dcomms.Sandbox;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.PocTest1
{
    public class Poc1Model : IDisposable
    {
        public VisionChannel1 VisionChannel { get; set; } = new VisionChannel1(true);
        public DrpTester5 DrpTester5 { get; set; }
        public Poc1Model(bool drpTester5_InitializeUser1EchoResponder = false)
        {
            VisionChannel.AttentionLevel = AttentionLevel.deepDetail;
            VisionChannel.DisplayedLogMessagesMaxCount = 1000;
            VisionChannel.ClearLog_RamSizeMB = 100;
            VisionChannel.ClearLog_MessagesCount = 1000; 

            DrpTester5 = new DrpTester5(VisionChannel);
            if (drpTester5_InitializeUser1EchoResponder)
            {
                DrpTester5.InitializeUser1EchoResponder.Execute(null);
            }

            VisionChannel.SevereMessageEmitted += VisionChannel_SevereMessageEmitted;
        }

        private void VisionChannel_SevereMessageEmitted(VisionChannel1.LogMessage msg)
        {
            SevereError?.Invoke(msg.Message);
        }

        public event Action<string> SevereError;
        public void Dispose()
        {
            DrpTester5.Dispose();
        }
    }
}
