using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{
    public class NatTest1RequestPacket
    {
        // byte flags
        const byte FlagsMask_MustBeZero = 0b11110000;
        public uint Token32 { get; set; }

        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)PacketTypes.NatTest1Request);
            byte flags = 0;
            writer.Write(flags);
            writer.Write(Token32);
            return ms.ToArray();
        }

        public static NatTest1RequestPacket Decode(byte[] udpData)
        {
            var r = new NatTest1RequestPacket();
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();
           
            r.Token32 = reader.ReadUInt32();
            return r;
        }

    }
}
