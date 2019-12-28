using Dcomms.Cryptography;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    public class ContactInvitation
    {
        const byte Flags_MustBeZero = 0b11100000;
        /// <summary>
        /// = address of user who initiates the contact invitation
        /// </summary>
        public RegistrationId InvitationInitiatorRegistrationId;
        public byte[] ContactInvitationToken;
        
        public static ContactInvitation CreateNew(ICryptoLibrary cryptoLibrary, RegistrationId registrationId)
        {
            return new ContactInvitation
            {
                ContactInvitationToken = cryptoLibrary.GetRandomBytes(InviteRequestPacket.ContactInvitationTokenSize),
                InvitationInitiatorRegistrationId = registrationId
            };
        }

        private ContactInvitation()
        {
        }
        public static ContactInvitation DecodeFromUI(string encoded)
        {
            var data = Convert.FromBase64String(encoded);
            using var reader = BinaryProcedures.CreateBinaryReader(data, 0);
            var flags = reader.ReadByte();
            if ((flags & Flags_MustBeZero) != 0) throw new NotImplementedException();
            
            return new ContactInvitation
            {
                InvitationInitiatorRegistrationId = RegistrationId.Decode(reader),
                ContactInvitationToken = reader.ReadBytes(InviteRequestPacket.ContactInvitationTokenSize)
            };
        }
        public string EncodeForUI()
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var w);

            // byte flags
            w.Write((byte)0);

            InvitationInitiatorRegistrationId.Encode(w);

            if (ContactInvitationToken.Length != InviteRequestPacket.ContactInvitationTokenSize) throw new Exception();
            w.Write(ContactInvitationToken);

            return Convert.ToBase64String(ms.ToArray());
        }

    }
}
