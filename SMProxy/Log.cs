using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Craft.Net.Networking;
using Craft.Net.Common;

namespace SMProxy
{
    public class Log
    {
        public StreamWriter Stream { get; set; }
        private MemoryStream MemoryStream { get; set; } // Used for getting raw packet data
        private MinecraftStream MinecraftStream { get; set; } // Used for getting raw packet data
        private ProxySettings Settings { get; set; }

        public Log(StreamWriter stream, ProxySettings settings)
        {
            Stream = stream;
            MemoryStream = new MemoryStream();
            MinecraftStream = new MinecraftStream(MemoryStream);
            Settings = settings;
        }

        public virtual void LogPacket(IPacket packet, bool clientToServer)
        {
            Stream.Write(PacketLogger.LogPacket(packet, clientToServer ? Craft.Net.PacketDirection.Serverbound : Craft.Net.PacketDirection.Clientbound));
        }

        private string FormatPacketName(string name)
        {
            if (name.EndsWith("Packet"))
                name = name.Remove(name.Length - "Packet".Length);
            // TODO: Consider adding spaces before capital letters
            return name;
        }

        public static string DumpArray(byte[] array)
        {
            if (array.Length == 0)
                return "[]";
            var sb = new StringBuilder((array.Length * 2) + 2);
            foreach (byte b in array)
                sb.AppendFormat("{0} ", b.ToString("X2"));
            return "[" + sb.ToString().Remove(sb.Length - 1) + "]";
        }

        public static string DumpArrayPretty(byte[] array)
        {
            if (array.Length == 0)
                return "[Empty arry]";
            int length = 5 * array.Length + (4 * (array.Length / 16)) + 2; // rough estimate of final length
            var sb = new StringBuilder(length);
            sb.AppendLine("[");
            for (int i = 0; i < array.Length; i += 16)
            {
                sb.Append(" ");
                // Hex dump
                int hexCount = 16;
                for (int j = i; j < array.Length && j < i + 16; j++, hexCount--)
                    sb.AppendFormat("{0} ", array[j].ToString("X2"));
                sb.Append(" ");
                for (; hexCount > 0; hexCount--)
                    sb.Append("   ");
                for (int j = i; j < array.Length && j < i + 16; j++)
                {
                    char value = Encoding.ASCII.GetString(new byte[] { array[j] })[0];
                    if (char.IsLetterOrDigit(value))
                        sb.AppendFormat("{0} ", value);
                    else
                        sb.Append(". ");
                }
                sb.AppendLine();
            }
            sb.AppendLine("]");
            string result = " " + sb.ToString().Replace("\n", "\n ");
            return result.Remove(result.Length - 2);
        }

        public static string AddSpaces(string value)
        {
            string newValue = "";
            foreach (char c in value)
            {
                if (char.IsLower(c))
                    newValue += c;
                else
                    newValue += " " + c;
            }
            return newValue.Substring(1);
        }

        public virtual void Write(string p)
        {
            Stream.Write(DateTime.Now.ToString("{hh:mm:ss.fff} "));
            Stream.WriteLine(p);
        }
    }
}
