using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// gets copied from request/response packets (REQ, ACK1, ACK2, CFM) to "neighborPeerACK" packet and "failure" packet
    /// </summary>
    public class RequestP2pSequenceNumber16
    {
        public ushort Seq16;
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Seq16);
        }
        public static RequestP2pSequenceNumber16 Decode(BinaryReader reader)
        {
            var r = new RequestP2pSequenceNumber16();
            r.Seq16 = reader.ReadUInt16();
            return r;
        }
        public override bool Equals(object obj)
        {
            return ((RequestP2pSequenceNumber16)obj).Seq16 == this.Seq16;
        }
        public override string ToString() => $"rSeq{Seq16}";
    }
}
