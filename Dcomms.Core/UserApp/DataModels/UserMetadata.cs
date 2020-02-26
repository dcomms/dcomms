using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.UserApp.DataModels
{
    public class UserMetadata
    {
        const byte FlagsMask_MustBeZero = 0b11100000;
        const byte FlagsMask_ContactCreatedAtUTC = 0b00000001;
        const byte FlagsMask_ContactCreatedWithRemoteEndpoint = 0b00000010;

        public DateTime? ContactCreatedAtUTC;
        public IPEndPoint ContactCreatedWithRemoteEndpoint;


        public void Encode(BinaryWriter writer)
        {
            byte flags = 0;
            if (ContactCreatedAtUTC.HasValue) flags |= FlagsMask_ContactCreatedAtUTC;
            if (ContactCreatedWithRemoteEndpoint != null) flags |= FlagsMask_ContactCreatedWithRemoteEndpoint;
            writer.Write(flags);

            if (ContactCreatedAtUTC.HasValue) writer.Write(ContactCreatedAtUTC.Value.ToBinary());
            if (ContactCreatedWithRemoteEndpoint != null) BinaryProcedures.EncodeIPEndPoint(writer, ContactCreatedWithRemoteEndpoint);
        }
        public byte[] Encode()
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }




        public static UserMetadata Decode(BinaryReader reader)
        {
            var r = new UserMetadata();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            if ((flags & FlagsMask_ContactCreatedAtUTC) != 0) r.ContactCreatedAtUTC = DateTime.FromBinary(reader.ReadInt64());
            if ((flags & FlagsMask_ContactCreatedWithRemoteEndpoint) != 0) r.ContactCreatedWithRemoteEndpoint = BinaryProcedures.DecodeIPEndPoint(reader);

            return r;
        }
        public static UserMetadata Decode(byte[] data)
        {
            if (data == null) return null;
            using var reader = BinaryProcedures.CreateBinaryReader(data, 0);
            return Decode(reader);
        }
    }
}
