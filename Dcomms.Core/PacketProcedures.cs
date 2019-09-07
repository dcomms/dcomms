using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms
{
    public static class PacketProcedures
    {

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
        public static void EncodeByteArray256(BinaryWriter writer, byte[] value)
        {
            if (value == null)
            {
                writer.Write((byte)0);
                return;
            }
            if (value.Length == 0) throw new ArgumentNullException();
            if (value.Length > 255) throw new ArgumentException(nameof(value));
            writer.Write((byte)value.Length);
            writer.Write(value);
        }
        public static void EncodeByteArray65536(BinaryWriter writer, byte[] value)
        {
            if (value == null)
            {
                writer.Write((ushort)0);
                return;
            }
            if (value.Length == 0) throw new ArgumentNullException();
            if (value.Length > 65535) throw new ArgumentException(nameof(value));
            writer.Write((ushort)value.Length);
            writer.Write(value);
        }




        /// <summary>
        /// encodes null value as empty string
        /// </summary>
        public static void EncodeString1UTF8(BinaryWriter writer, string value)
        {
            if (value == null) value = "";
            var valueBytes = Encoding.UTF8.GetBytes(value);
            if (valueBytes.Length > 255) throw new ArgumentException(nameof(value));
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
        public static string DecodeString1UTF8(BinaryReader reader)
        {
            var length = reader.ReadByte();
            var stringData = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(stringData);
        }
        public static byte[] DecodeByteArray256(BinaryReader reader)
        {
            var length = reader.ReadByte();
            if (length == 0) return null;
            return reader.ReadBytes(length);
        }
        public static byte[] DecodeByteArray65536(BinaryReader reader)
        {
            var length = reader.ReadUInt16();
            if (length == 0) return null;
            return reader.ReadBytes(length);
        }

        public static void EncodeIPEndPointIpv4(BinaryWriter writer, IPEndPoint endpoint)
        {
            if (endpoint.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException();
            writer.Write(endpoint.Address.GetAddressBytes());
            writer.Write((UInt16)endpoint.Port);
        }
        public static IPEndPoint DecodeIPEndPointIpv4(BinaryReader reader)
        {
            return new IPEndPoint(new IPAddress(reader.ReadBytes(4)), reader.ReadUInt16());
        }

        public static void EncodeIPEndPoint(BinaryWriter writer, IPEndPoint endpoint)
        {
            EncodeByteArray256(writer, endpoint.Address.GetAddressBytes());
            writer.Write((UInt16)endpoint.Port);
        }
        public static IPEndPoint DecodeIPEndPoint(BinaryReader reader)
        {
            return new IPEndPoint(new IPAddress(DecodeByteArray256(reader)), reader.ReadUInt16());
        }


    }
}
