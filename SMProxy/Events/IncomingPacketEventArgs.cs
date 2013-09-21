using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Craft.Net.Networking;

namespace SMProxy.Events
{
    public class IncomingPacketEventArgs : EventArgs
    {
        public IncomingPacketEventArgs(IPacket packet, bool clientToServer)
        {
            Packet = packet;
            ClientToServer = clientToServer;
            Handled = false;
        }
        
        public IPacket Packet { get; set; }
        public bool ClientToServer { get; set; }
        public bool Handled { get; set; }
    }
}
