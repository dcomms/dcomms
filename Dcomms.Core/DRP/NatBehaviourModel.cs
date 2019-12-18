using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP
{
    public class NatBehaviourModel
    {
        //  byte flags0;
        const byte Flags0Mask_MustBeZero = 0b11110000;
        //  byte flags1;

        public bool StaticIp_LongTerm { get; set; }
        const byte StaticIp_LongTerm_Flags1Mask = 0b00000001;
        public bool PortsMappingIsStatic_LongTerm { get; set; }
        const byte PortsMappingIsStatic_LongTerm_Flags1Mask = 0b00000010;

        public bool UpnpWorks { get; set; }
        const byte UpnpWorks_Flags1Mask = 0b00000100;
        public bool PortsMappingIsStatic_ShortTerm { get; set; }
        const byte PortsMappingIsStatic_ShortTerm_Flags1Mask = 0b00001000;
        public bool IsAccessibleFromNewUnknownRequesterIp { get; set; }
        const byte IsAccessibleFromNewUnknownRequesterIp_Flags1Mask = 0b00010000;
        public bool NewUnknownRequesterBeforeLocalRequestIsBanned { get; set; } 
        const byte NewUnknownRequesterBeforeLocalRequestIsBanned_Flags1Mask = 0b00100000;

        public bool IsAccessibleOnlyAfterLocalRequest { get; set; } // restricted code, symmetric NAT
        const byte IsAccessibleOnlyAfterLocalRequest_Flags1Mask = 0b01000000;

        public bool PublicIpIsAccessibleFromSameIp { get; set; }
        const byte PublicIpIsAccessibleFromSameIp_Flags1Mask = 0b10000000;


        public void Encode(BinaryWriter writer)
        {
            byte flags0 = 0;
            writer.Write(flags0);

            byte flags1 = 0;
            if (StaticIp_LongTerm) flags1 |= StaticIp_LongTerm_Flags1Mask;
            if (PortsMappingIsStatic_LongTerm) flags1 |= PortsMappingIsStatic_LongTerm_Flags1Mask;
            if (UpnpWorks) flags1 |= UpnpWorks_Flags1Mask;
            if (PortsMappingIsStatic_ShortTerm) flags1 |= PortsMappingIsStatic_ShortTerm_Flags1Mask;
            if (IsAccessibleFromNewUnknownRequesterIp) flags1 |= IsAccessibleFromNewUnknownRequesterIp_Flags1Mask;
            if (NewUnknownRequesterBeforeLocalRequestIsBanned) flags1 |= NewUnknownRequesterBeforeLocalRequestIsBanned_Flags1Mask;
            if (IsAccessibleOnlyAfterLocalRequest) flags1 |= IsAccessibleOnlyAfterLocalRequest_Flags1Mask;
            if (PublicIpIsAccessibleFromSameIp) flags1 |= PublicIpIsAccessibleFromSameIp_Flags1Mask;
            writer.Write(flags1);
        }
        public static NatBehaviourModel Decode(BinaryReader reader)
        {
            var r = new NatBehaviourModel();
            var flags0 = reader.ReadByte();
            if ((flags0 & Flags0Mask_MustBeZero) != 0) throw new NotImplementedException();

            var flags1 = reader.ReadByte();
            if ((flags1 & StaticIp_LongTerm_Flags1Mask) != 0) r.StaticIp_LongTerm = true;
            if ((flags1 & PortsMappingIsStatic_LongTerm_Flags1Mask) != 0) r.PortsMappingIsStatic_LongTerm = true;
            if ((flags1 & UpnpWorks_Flags1Mask) != 0) r.UpnpWorks = true;
            if ((flags1 & PortsMappingIsStatic_ShortTerm_Flags1Mask) != 0) r.PortsMappingIsStatic_ShortTerm = true;
            if ((flags1 & IsAccessibleFromNewUnknownRequesterIp_Flags1Mask) != 0) r.IsAccessibleFromNewUnknownRequesterIp = true;
            if ((flags1 & NewUnknownRequesterBeforeLocalRequestIsBanned_Flags1Mask) != 0) r.NewUnknownRequesterBeforeLocalRequestIsBanned = true;
            if ((flags1 & IsAccessibleOnlyAfterLocalRequest_Flags1Mask) != 0) r.IsAccessibleOnlyAfterLocalRequest = true;
            if ((flags1 & PublicIpIsAccessibleFromSameIp_Flags1Mask) != 0) r.PublicIpIsAccessibleFromSameIp = true;

            return r;
        }

        public static NatBehaviourModel Unknown => new NatBehaviourModel
        {
            IsAccessibleOnlyAfterLocalRequest = true,
            
        };
    }
}
