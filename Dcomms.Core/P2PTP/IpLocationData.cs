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
                Country = P2ptpCommon.DecodeString1UTF8(reader),
                CountryCode = P2ptpCommon.DecodeString1UTF8(reader),
                City = P2ptpCommon.DecodeString1UTF8(reader),
                State = P2ptpCommon.DecodeString1UTF8(reader),
                StateCode = P2ptpCommon.DecodeString1UTF8(reader),
                ZIP = P2ptpCommon.DecodeString1UTF8(reader),
                Organization_ISP = P2ptpCommon.DecodeString1UTF8(reader),
                AS = P2ptpCommon.DecodeString1UTF8(reader),
                ASname = P2ptpCommon.DecodeString1UTF8(reader),
            };
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            writer.Write(Longitude);
            writer.Write(Latitude);
            P2ptpCommon.EncodeString1UTF8(writer, Country);
            P2ptpCommon.EncodeString1UTF8(writer, CountryCode);
            P2ptpCommon.EncodeString1UTF8(writer, City);
            P2ptpCommon.EncodeString1UTF8(writer, State);
            P2ptpCommon.EncodeString1UTF8(writer, StateCode);
            P2ptpCommon.EncodeString1UTF8(writer, ZIP);
            P2ptpCommon.EncodeString1UTF8(writer, Organization_ISP);
            P2ptpCommon.EncodeString1UTF8(writer, AS);
            P2ptpCommon.EncodeString1UTF8(writer, ASname);
        }
    }
}
