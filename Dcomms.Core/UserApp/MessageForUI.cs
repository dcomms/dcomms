using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    public class MessageForUI
    {
        private static int _idCounter = 0;
        private readonly int _id = ++_idCounter;
        public override string ToString()
        {
            return $"msg{_id}";
        }

        public string Text { get; set; }
        public bool IsDelivered { get; set; }
        /// <summary>
        /// true - sent
        /// false - received
        /// </summary>
        public bool IsOutgoing { get; set; }

        /// <summary>
        /// sent or received
        /// </summary>
        public DateTime LocalCreationTimeUTC { get; set; }
    }
}
