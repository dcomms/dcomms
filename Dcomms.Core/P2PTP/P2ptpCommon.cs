using System;
using System.IO;
using System.Text;

namespace Dcomms.P2PTP
{
    public static class P2ptpCommon
    {
        public const ushort ProtocolVersion = 1;

        static bool DecodeValidSignature(byte[] data)
        {
            var s = PacketHeaders.P2PTP;
            return data.Length >= HeaderSize &&
                data[0] == s[0] && data[1] == s[1] && data[2] == s[2] && data[3] == s[3];
        }
        public static PacketType? DecodeHeader(byte[] data)
        {
            if (!DecodeValidSignature(data)) return null;
            return (PacketType)data[4];
        }
        public const int HeaderSize = 5;
        public static void EncodeHeader(byte[] data, PacketType packetType)
        {
            var s = PacketHeaders.P2PTP;
            data[0] = s[0];
            data[1] = s[1];
            data[2] = s[2];
            data[3] = s[3];
            data[4] = (byte)packetType;
        }
        public static void EncodeHeader(BinaryWriter writer, PacketType packetType)
        {
            writer.Write(PacketHeaders.P2PTP);
            writer.Write((byte)packetType);
        }
        

        public static void EncodeUInt16(byte[] data, ref int index, UInt16 value)
        {
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF);
        }
        public static void EncodeUInt32(byte[] data, ref int index, UInt32 value)
        {
            data[index++] = (byte)(value & 0x000000FF); value >>= 8;
            data[index++] = (byte)(value & 0x000000FF); value >>= 8;
            data[index++] = (byte)(value & 0x000000FF); value >>= 8;
            data[index++] = (byte)(value & 0x000000FF);
        }
        public static void EncodeUInt32(BinaryWriter writer, uint value)
        {
            writer.Write(value);
        }
        /// <summary>
        /// length = string length + 1 byte
        /// </summary>
        public static void EncodeString1ASCII(byte[] data, ref int index, string value)
        {
            if (value.Length > 255) throw new ArgumentException(nameof(value));

            var valueBytes = Encoding.ASCII.GetBytes(value);
            data[index++] = (byte)valueBytes.Length;
            Array.Copy(valueBytes, 0, data, index, valueBytes.Length);
            index += valueBytes.Length;
        }
        /// <summary>
        /// encodes null value as empty string
        /// </summary>
        public static void EncodeString1ASCII(BinaryWriter writer, string value)
        {
            if (value == null) value = "";
            if (value.Length > 255) throw new ArgumentException(nameof(value));
            var valueBytes = Encoding.ASCII.GetBytes(value);
            writer.Write((byte)valueBytes.Length);
            writer.Write(valueBytes);
        }
        public static uint DecodeUInt32(byte[] data, ref int index)
        {
            return ((uint)data[index++]) | ((uint)data[index++] << 8) | ((uint)data[index++] << 16) | ((uint)data[index++] << 24);
        }
        public static uint DecodeUInt32(BinaryReader reader)
        {
            return reader.ReadUInt32();
        }
        public static ushort DecodeUInt16(byte[] data, ref int index)
        {
            return (ushort)(((ushort)data[index++]) | ((ushort)data[index++] << 8));
        }
        public static byte DecodeByte(byte[] data, ref int index)
        {
            return data[index++];
        }


        public static void EncodeUInt64(byte[] data, ref int index, UInt64 value)
        {
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF);
        }
        public static UInt64 DecodeUInt64(byte[] data, ref int index)
        {
            return ((UInt64)data[index++]) | ((UInt64)data[index++] << 8) | ((UInt64)data[index++] << 16) | ((UInt64)data[index++] << 24)
                | ((UInt64)data[index++] << 32) | ((UInt64)data[index++] << 40) | ((UInt64)data[index++] << 48) | ((UInt64)data[index++] << 66);
        }

        public static void EncodeInt64(byte[] data, ref int index, Int64 value)
        {
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF); value >>= 8;
            data[index++] = (byte)(value & 0xFF);
        }
        public static BinaryReader CreateBinaryReader(byte[] data, int index)
        {
            return new BinaryReader(new MemoryStream(data, index, data.Length - index), Encoding.UTF8);
        }
        public static void CreateBinaryWriter(out MemoryStream ms, out BinaryWriter writer)
        {
            ms = new MemoryStream();
            writer = new BinaryWriter(ms, Encoding.UTF8);
        }
        public static Int64 DecodeInt64(byte[] data, ref int index)
        {
            return ((Int64)data[index++]) | ((Int64)data[index++] << 8) | ((Int64)data[index++] << 16) | ((Int64)data[index++] << 24)
                | ((Int64)data[index++] << 32) | ((Int64)data[index++] << 40) | ((Int64)data[index++] << 48) | ((Int64)data[index++] << 66);
        }
        public static string DecodeString1ASCII(byte[] data, ref int index)
        {
            var length = data[index++];
            var r = Encoding.ASCII.GetString(data, index, length);
            index += length;
            return r;
        }
        public static string DecodeString1ASCII(BinaryReader reader)
        {
            var length = reader.ReadByte();
            var stringData = reader.ReadBytes(length);
            return Encoding.ASCII.GetString(stringData);
        }



        static readonly DateTime BaseDate = new DateTime(2019, 1, 1);
        static readonly DateTime MaxDate = BaseDate.AddSeconds(Math.Pow(2, 32));
        internal static uint ToUInt32(this DateTime date)
        {
            if (date < BaseDate) throw new ArgumentException();
            if (date >= MaxDate) throw new ArgumentException();
            return (uint)((date - BaseDate).TotalSeconds);
        }
        internal static DateTime FromUInt32(this uint v)
        {
            return BaseDate.AddSeconds(v);
        }

    }

    /// <summary>
    /// contains unique, known headers, i.e. first bytes in UDP packets
    /// </summary>
    public static class PacketHeaders
    {
        public static readonly byte[] P2PTP = new byte[] { 154, 77, 200, 11 };
        /// <summary>
        /// main UDP payload, used by SUBT extension
        /// </summary>
        public static readonly byte[] SubtPayload = new byte[] { 104, 78 };

    }

    /// <summary>
    /// specifies type of the P2PTP packet
    /// takes 1 byte
    /// </summary>
    public enum PacketType
    {      
        hello = 1,

        /// <summary>
        /// is shared regularly between peers
        /// is accepted by client peers
        /// is ignored by server peers. servers themselves know many connected clients and send the peersList to the clients, so clients know about each other
        /// </summary>
        peersListIpv4 = 5,
        /// <summary>
        /// reserved for future use
        /// </summary>
        peersListIpv4and6 = 6,      
        
        /// <summary>
        /// contain ensapsulated extension-specific data
        /// are processed by manager thread
        /// </summary>
        extensionSignaling = 7,
    }
   
    public class PeerId
    {
        public string GuidString => Guid.ToString();
        public readonly Guid Guid;
        public PeerId(Guid guid)
        {
            if (guid.Equals(Guid.Empty)) throw new ArgumentException(nameof(guid));
            Guid = guid;
        }
        public override string ToString()
        {
            return $"{Guid}";
        }
        public override bool Equals(object obj)
        {
            var obj2 = (PeerId)obj;
            return obj2.Guid.Equals(this.Guid);
        }
        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }

        public const int EncodedSize = 16;
        public static void Encode(PeerId testNodeId, byte[] data, ref int index)
        {
            var guidBytes = (testNodeId?.Guid ?? Guid.Empty).ToByteArray();
            Array.Copy(guidBytes, 0, data, index, EncodedSize);
            index += EncodedSize;
        }
        public static void Encode(BinaryWriter writer, PeerId testNodeId)
        {
            var guidBytes = (testNodeId?.Guid ?? Guid.Empty).ToByteArray();
            writer.Write(guidBytes);
        }
        public static PeerId Decode(byte[] data, ref int index)
        {
            var guidBytes = new byte[EncodedSize];
            Array.Copy(data, index, guidBytes, 0, EncodedSize);
            index += EncodedSize;
            var guid = new Guid(guidBytes);
            if (guid.Equals(Guid.Empty)) return null;
            else return new PeerId(guid);
        }
        public static PeerId Decode(BinaryReader reader)
        {
            var guidBytes = reader.ReadBytes(EncodedSize);
            var guid = new Guid(guidBytes);
            if (guid.Equals(Guid.Empty)) return null;
            else return new PeerId(guid);
        }
    }

    /// <summary>
    /// is generated by peer that initially sends 'setup'
    /// uniquely identifies the stream (connection to another peer) within local peer, and all connected peers
    /// is used by SUBT extension to quickly find corresponding stream, within receiver thread
    /// </summary>
    public class StreamId
    {
        public readonly uint Id;
        public StreamId(uint id)
        {
            if (id == 0) throw new ArgumentException(nameof(id));
            Id = id;
        }
        public override string ToString()
        {
            return $"{Id}";
        }
        public override bool Equals(object obj)
        {
            // for dictionary of remote peers: dates are not included, as they are dynamic

            var obj2 = (StreamId)obj;
            return obj2.Id == this.Id;
        }
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public const int EncodedSize = 4;
        public static void Encode(StreamId streamId, byte[] data, ref int index)
        {
            P2ptpCommon.EncodeUInt32(data, ref index, streamId?.Id ?? 0);
        }
        public static void Encode(BinaryWriter writer, StreamId streamId)
        {
            P2ptpCommon.EncodeUInt32(writer, streamId?.Id ?? 0);
        }
        public static StreamId Decode(byte[] data, ref int index)
        {
            var id = P2ptpCommon.DecodeUInt32(data, ref index);
            if (id == 0) return null;
            else return new StreamId(id);
        }
        public static StreamId Decode(BinaryReader reader)
        {
            var id = P2ptpCommon.DecodeUInt32(reader);
            if (id == 0) return null;
            else return new StreamId(id);
        }
    }

    class InsufficientResourcesException: ApplicationException
    {

    }
}
