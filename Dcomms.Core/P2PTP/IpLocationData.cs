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
                Country = P2ptpCommon.DecodeString1ASCII(reader),
                CountryCode = P2ptpCommon.DecodeString1ASCII(reader),
                City = P2ptpCommon.DecodeString1ASCII(reader),
                State = P2ptpCommon.DecodeString1ASCII(reader),
                StateCode = P2ptpCommon.DecodeString1ASCII(reader),
                ZIP = P2ptpCommon.DecodeString1ASCII(reader),
                Organization_ISP = P2ptpCommon.DecodeString1ASCII(reader),
                AS = P2ptpCommon.DecodeString1ASCII(reader),
                ASname = P2ptpCommon.DecodeString1ASCII(reader),
            };
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            writer.Write(Longitude);
            writer.Write(Latitude);
            P2ptpCommon.EncodeString1ASCII(writer, Country);
            P2ptpCommon.EncodeString1ASCII(writer, CountryCode);
            P2ptpCommon.EncodeString1ASCII(writer, City);
            P2ptpCommon.EncodeString1ASCII(writer, State);
            P2ptpCommon.EncodeString1ASCII(writer, StateCode);
            P2ptpCommon.EncodeString1ASCII(writer, ZIP);
            P2ptpCommon.EncodeString1ASCII(writer, Organization_ISP);
            P2ptpCommon.EncodeString1ASCII(writer, AS);
            P2ptpCommon.EncodeString1ASCII(writer, ASname);
        }
    }
}
