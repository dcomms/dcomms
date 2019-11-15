using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.P2PTP
{
    /// <summary>
    /// encoded in hello packet
    /// </summary>
    public class IpLocationData
    {
        public byte Flags; // bool? mobile, proxy;
        public double Longitude, Latitude;
        public string Country, CountryCode, City, State, StateCode, ZIP;
        public string Organization_ISP, AS, ASname;

        public override string ToString()
        {
            return $"{Country}, {State}, {City}";
        }

        public static IpLocationData Decode(BinaryReader reader)
        {            
            return new IpLocationData
            {
                Flags = reader.ReadByte(),
                Longitude = reader.ReadDouble(),
                Latitude = reader.ReadDouble(),
                Country = PacketProcedures.DecodeString1UTF8(reader),
                CountryCode = PacketProcedures.DecodeString1UTF8(reader),
                City = PacketProcedures.DecodeString1UTF8(reader),
                State = PacketProcedures.DecodeString1UTF8(reader),
                StateCode = PacketProcedures.DecodeString1UTF8(reader),
                ZIP = PacketProcedures.DecodeString1UTF8(reader),
                Organization_ISP = PacketProcedures.DecodeString1UTF8(reader),
                AS = PacketProcedures.DecodeString1UTF8(reader),
                ASname = PacketProcedures.DecodeString1UTF8(reader),
            };
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            writer.Write(Longitude);
            writer.Write(Latitude);
            PacketProcedures.EncodeString1UTF8(writer, Country);
            PacketProcedures.EncodeString1UTF8(writer, CountryCode);
            PacketProcedures.EncodeString1UTF8(writer, City);
            PacketProcedures.EncodeString1UTF8(writer, State);
            PacketProcedures.EncodeString1UTF8(writer, StateCode);
            PacketProcedures.EncodeString1UTF8(writer, ZIP);
            PacketProcedures.EncodeString1UTF8(writer, Organization_ISP);
            PacketProcedures.EncodeString1UTF8(writer, AS);
            PacketProcedures.EncodeString1UTF8(writer, ASname);
        }
    }
}
