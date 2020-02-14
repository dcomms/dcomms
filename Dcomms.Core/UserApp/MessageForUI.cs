using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    public class MessageForUI
    {
        public string Text { get; set; }
        public bool IsDelivered { get; set; }
        /// <summary>
        /// true - sent
        /// false - received
        /// </summary>
        public bool IsOutgoing { get; set; }
    }
}
