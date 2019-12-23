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
                Country = BinaryProcedures.DecodeString1UTF8(reader),
                CountryCode = BinaryProcedures.DecodeString1UTF8(reader),
                City = BinaryProcedures.DecodeString1UTF8(reader),
                State = BinaryProcedures.DecodeString1UTF8(reader),
                StateCode = BinaryProcedures.DecodeString1UTF8(reader),
                ZIP = BinaryProcedures.DecodeString1UTF8(reader),
                Organization_ISP = BinaryProcedures.DecodeString1UTF8(reader),
                AS = BinaryProcedures.DecodeString1UTF8(reader),
                ASname = BinaryProcedures.DecodeString1UTF8(reader),
            };
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            writer.Write(Longitude);
            writer.Write(Latitude);
            BinaryProcedures.EncodeString1UTF8(writer, Country);
            BinaryProcedures.EncodeString1UTF8(writer, CountryCode);
            BinaryProcedures.EncodeString1UTF8(writer, City);
            BinaryProcedures.EncodeString1UTF8(writer, State);
            BinaryProcedures.EncodeString1UTF8(writer, StateCode);
            BinaryProcedures.EncodeString1UTF8(writer, ZIP);
            BinaryProcedures.EncodeString1UTF8(writer, Organization_ISP);
            BinaryProcedures.EncodeString1UTF8(writer, AS);
            BinaryProcedures.EncodeString1UTF8(writer, ASname);
        }
    }
}
