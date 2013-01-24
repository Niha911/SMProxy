using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace SMProxy
{
    public class ProxySettings
    {
        public ProxySettings()
        {
            // Default settings
            LogServer = LogClient = true;
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 25564);
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 25565);
            PacketFilter = new List<byte>();
            for (int i = 0; i < 256; i++) PacketFilter.Add((byte)i);
        }

        /// <summary>
        /// If false, server->client packets are omitted from
        /// the log.
        /// </summary>
        public bool LogServer { get; set; }
        /// <summary>
        /// If false, client->server packets are omitted from
        /// the log.
        /// </summary>
        public bool LogClient { get; set; }
        /// <summary>
        /// The local endpoint to listen for connections on.
        /// </summary>
        public IPEndPoint LocalEndPoint { get; set; }
        /// <summary>
        /// The server to connect to.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; set; }
        /// <summary>
        /// All packet IDs in this list are included in the log.
        /// </summary>
        public List<byte> PacketFilter { get; set; }
        /// <summary>
        /// Minecraft.net username
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Minecraft.net password
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// True if the proxy is to authenticate clients with
        /// minecraft.net.
        /// </summary>
        public bool AuthenticateClients { get; set; }
    }
}
