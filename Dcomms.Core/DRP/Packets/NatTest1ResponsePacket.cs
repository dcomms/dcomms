using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP.Packets
{
    public class NatTest1ResponsePacket
    {
        // byte flags
        const byte FlagsMask_MustBeZero = 0b11110000;
        public uint Token32 { get; set; }
        public IPEndPoint RequesterEndpoint { get; set; }

        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)PacketTypes.NatTest1Response);
            byte flags = 0;           
            writer.Write(flags);
            writer.Write(Token32);
            PacketProcedures.EncodeIPEndPoint_ipv4(writer, RequesterEndpoint);
            return ms.ToArray();
        }
        public static NatTest1ResponsePacket Decode(byte[] udpData)
        {
            var r = new NatTest1ResponsePacket();
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0)
                throw new NotImplementedException();

            r.Token32 = reader.ReadUInt32();
            r.RequesterEndpoint = PacketProcedures.DecodeIPEndPoint_ipv4(reader);
            return r;
        }
        public static LowLevelUdpResponseScanner GetScanner(uint token32)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)PacketTypes.NatTest1Response);
            byte flags = 0;
            writer.Write(flags);
            writer.Write(token32);
            return new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
        }
    }
}
