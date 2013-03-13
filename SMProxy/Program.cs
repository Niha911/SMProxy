using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Globalization;
using SMProxy.Plugins;
using SMProxy.Events;

namespace SMProxy
{
    class Program
    {
        private static TcpListener Listener { get; set; }
        private static List<Proxy> Sessions { get; set; }
        private static ProxySettings ProxySettings { get; set; }
        private static List<IPlugin> Plugins { get; set; }

        public static event EventHandler<UnrecognizedArgumentEventArgs> UnrecognizedArgument;

        static void Main(string[] args)
        {
            ProxySettings = new ProxySettings();
            // Load plugins into AppDomain
            foreach (var plugin in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.dll"))
            {
                try
                {
                    Assembly.LoadFile(plugin);
                }
                catch { }
            }
            LoadPlugins();

            bool remoteSet = false, localSet = false;
            // Interpret command line args
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("-"))
                {
                    switch (arg)
                    {
                        case "--local-endpoint":
                            ProxySettings.LocalEndPoint = ParseEndPoint(args[++i]);
                            break;
                        case "--remote-endpoint":
                            ProxySettings.RemoteEndPoint = ParseEndPoint(args[++i], 25565);
                            break;
                        case "--filter":
                            ProxySettings.PacketFilter = ParseFilter(args[++i]);
                            break;
                        case "--unfilter":
                            var filter = ParseFilter(args[++i]);
                            ProxySettings.PacketFilter.RemoveAll(p => filter.Contains(p));
                            break;
                        case "--omit-client":
                            ProxySettings.LogClient = false;
                            break;
                        case "--omit-server":
                            ProxySettings.LogServer = false;
                            break;
                        case "--username":
                            ProxySettings.Username = args[++i];
                            break;
                        case "--password":
                            ProxySettings.Password = args[++i];
                            break;
                        case "--help":
                            DisplayHelp();
                            return;
                        default:
                            var eventArgs = new UnrecognizedArgumentEventArgs
                            {
                                Argument = arg,
                                Args = args,
                                Index = i,
                                Handled = false
                            };
                            if (UnrecognizedArgument != null)
                                UnrecognizedArgument(null, eventArgs);
                            if (!eventArgs.Handled)
                            {
                                Console.WriteLine("Invalid command line arguments. Use --help for more information.");
                                return;
                            }
                            break;
                    }
                }
                else
                {
                    if (!remoteSet)
                    {
                        ProxySettings.RemoteEndPoint = ParseEndPoint(arg, 25565);
                        remoteSet = true;
                    }
                    else if (!localSet)
                    {
                        ProxySettings.LocalEndPoint = ParseEndPoint(arg);
                        localSet = true;
                    }
                    else
                    {
                        var eventArgs = new UnrecognizedArgumentEventArgs
                        {
                            Argument = arg,
                            Args = args,
                            Index = i,
                            Handled = false
                        };
                        if (UnrecognizedArgument != null)
                            UnrecognizedArgument(null, eventArgs);
                        if (!eventArgs.Handled)
                        {
                            Console.WriteLine("Invalid command line arguments. Use --help for more information.");
                            return;
                        }
                    }
                }
            }

            if (ProxySettings.Password == null)
            {
                // Grab lastlogin if possible
                var login = Minecraft.GetLastLogin();
                if (login != null)
                {
                    if (ProxySettings.Username == null)
                        ProxySettings.Username = login.Username;
                    ProxySettings.Password = login.Password;
                }
            }

            Listener = new TcpListener(ProxySettings.LocalEndPoint);
            Sessions = new List<Proxy>();
            Listener.Start();
            Listener.BeginAcceptTcpClient(AcceptClient, null);

            Console.WriteLine("Proxy started on " + ProxySettings.LocalEndPoint);

            Console.WriteLine("Press 'q' to exit.");

            ConsoleKeyInfo cki = new ConsoleKeyInfo();
            do { cki = Console.ReadKey(); } while (cki.KeyChar != 'q');
        }

        private static void AcceptClient(IAsyncResult result)
        {
            var client = Listener.EndAcceptTcpClient(result);
            Console.WriteLine("New connection from " + client.Client.RemoteEndPoint.ToString());
            // Connect to remote
            var server = new TcpClient();
            server.Connect(ProxySettings.RemoteEndPoint);
            var proxy = new Proxy(client.GetStream(), server.GetStream(),
                new Log(new StreamWriter(GetLogName()), ProxySettings), ProxySettings);
            Sessions.Add(proxy);
            foreach (var plugin in Plugins)
                plugin.SessionInitialize(proxy);
            proxy.Start();
            Listener.BeginAcceptTcpClient(AcceptClient, null);
        }

        private static void LoadPlugins()
        {
            var typeList = AppDomain.CurrentDomain.GetAssemblies().Select(a =>
                a.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface));
            Plugins = new List<IPlugin>();
            foreach (var list in typeList)
            {
                foreach (var type in list)
                {
                    var plugin = Activator.CreateInstance(type) as IPlugin;
                    plugin.GlobalInitialize();
                    Plugins.Add(plugin);
                }
            }
        }

        private static List<byte> ParseFilter(string filter)
        {
            var result = new List<byte>();
            var packets = filter.Split(',');
            foreach (var p in packets)
                result.Add(byte.Parse(p, NumberStyles.HexNumber));
            return result;
        }

        private static IPEndPoint ParseEndPoint(string arg, int defaultPort = 25564)
        {
            IPAddress address;
            int port;
            if (arg.Contains(':'))
            {
                // Both IP and port are specified
                var parts = arg.Split(':');
                if (!IPAddress.TryParse(parts[0], out address))
                    address = Resolve(parts[0]);
                return new IPEndPoint(address, int.Parse(parts[1]));
            }
            if (IPAddress.TryParse(arg, out address))
                return new IPEndPoint(address, defaultPort);
            if (int.TryParse(arg, out port))
                return new IPEndPoint(IPAddress.Loopback, port);
            return new IPEndPoint(Resolve(arg), defaultPort);
        }

        private static IPAddress Resolve(string arg)
        {
            return Dns.GetHostEntry(arg).AddressList.FirstOrDefault();
        }

        private static string GetLogName()
        {
            return "log_" + DateTime.Now.ToString("dd-MM-yyyy_HH-mm-ss") + ".txt";
        }

        private static void DisplayHelp()
        {
            Console.WriteLine("Usage: SMProxy.exe [options...] [remote endpoint] [local endpoint]");
            Console.WriteLine("Default remote: 127.0.0.1:25565; Default local: 127.0.0.1:25564");
            Console.WriteLine("Options:");
            Console.WriteLine("--local-endpoint [endpoint]: Specify the local endpoint");
            Console.WriteLine("--filter [packets...]: A hexadecimal, comma-delimited list of packets to log");
            Console.WriteLine("--omit-client: Omits client->server packets from the log");
            Console.WriteLine("--omit-server: Omits server->client packets from the log");
            Console.WriteLine("--password [password]: Specify a minecraft.net password");
            Console.WriteLine("--unfilter [packets]: A hexadecimal, comma-delimited list of packets NOT to log");
            Console.WriteLine("--username [name]: Specify a minecraft.net username");
        }
    }
}
