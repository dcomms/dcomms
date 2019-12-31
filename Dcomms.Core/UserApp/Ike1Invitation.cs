using Dcomms.Cryptography;
using Dcomms.DRP;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.UserApp
{
    public class Ike1Invitation
    {
        const byte Flags_MustBeZero = 0b11100000;
        /// <summary>
        /// = address of user who initiates the contact invitation
        /// </summary>
        public RegistrationId InvitationInitiatorRegistrationId;
        public byte[] ContactInvitationToken;
        
        public static Ike1Invitation CreateNew(ICryptoLibrary cryptoLibrary, RegistrationId registrationId)
        {
            return new Ike1Invitation
            {
                ContactInvitationToken = cryptoLibrary.GetRandomBytes(InviteRequestPacket.ContactInvitationTokenSize),
                InvitationInitiatorRegistrationId = registrationId
            };
        }

        private Ike1Invitation()
        {
        }
        public static Ike1Invitation DecodeFromUI(string encoded)
        {
            var data = Convert.FromBase64String(encoded);
            using var reader = BinaryProcedures.CreateBinaryReader(data, 0);
            var flags = reader.ReadByte();
            if ((flags & Flags_MustBeZero) != 0) throw new NotImplementedException();
            
            return new Ike1Invitation
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
