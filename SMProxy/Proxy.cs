﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Net;
using SMProxy.Events;
using Craft.Net.Common;
using Craft.Net.Client;
using Craft.Net.Networking;

namespace SMProxy
{
    public class Proxy
    {
        public Thread Worker { get; set; }
        public NetworkStream Client { get; set; }
        public NetworkStream Server { get; set; }
        public Log Log { get; set; }
        public ProxySettings Settings { get; set; }
        public NetworkManager ClientStream { get; set; }
        public NetworkManager ServerStream { get; set; }
        public string PlayerName { get; private set; }

        private byte[] ServerSharedKey { get; set; }
        private byte[] ClientSharedKey { get; set; }
        private RSAParameters ServerKey { get; set; }
        private RSACryptoServiceProvider CryptoServiceProvider { get; set; }
        // Temporary variables that are used while initializing encryption
        private EncryptionKeyRequestPacket ClientEncryptionRequest { get; set; }
        private EncryptionKeyResponsePacket ServerEncryptionResponse { get; set; }
        private byte[] ClientVerificationToken { get; set; }
        private string ClientAuthenticationHash { get; set; }

        public event EventHandler ConnectionClosed;
        /// <summary>
        /// Fired when a packet arrives, before it is proxied. Certain packets
        /// (namely, encryption-related ones) are handled by SMProxy explicitly,
        /// and this event is not called for those packets.
        /// </summary>
        public event EventHandler<IncomingPacketEventArgs> IncomingPacket;

        public Proxy(NetworkStream client, NetworkStream server, Log log, ProxySettings settings)
        {
            Client = client;
            Server = server;
            Log = log;
            ClientStream = new NetworkManager(Client); //ClientStream = new MinecraftStream(new BufferedStream(Client));
            ServerStream = new NetworkManager(Server); //ServerStream = new MinecraftStream(new BufferedStream(Server));
            CryptoServiceProvider = new RSACryptoServiceProvider(1024);
            ServerKey = CryptoServiceProvider.ExportParameters(true);
            Settings = settings;
        }

        public void Start()
        {
            Worker = new Thread(DoWork);
            Worker.Start();
        }

        public void Stop()
        {
            Worker.Abort();
        }

        private void DoWork()
        {
            // TODO: Fallback to raw proxy
            while (true)
            {
                try
                {
                    UpdateServer();
                    UpdateClient();
                    Thread.Sleep(1);
                }
                catch (Exception e)
                {
					Log.Write ("Unhandled exception in internal SMProxy tick: " + Environment.NewLine + 
					           e.ToString());
                    if (ConnectionClosed != null)
                        ConnectionClosed(this, null);
                    try { Client.Close(); } catch { }
                    try { Server.Close(); } catch { }
                    return;
                }
            }
        }

        private void UpdateClient()
        {
            while (Client.DataAvailable)
            {
                var packet = ClientStream.ReadPacket(Craft.Net.PacketDirection.Serverbound);
                Log.LogPacket(packet, true);

                if (packet is EncryptionKeyResponsePacket)
                {
                    if (!FinializeClientEncryption((EncryptionKeyResponsePacket)packet))
                    {
                        if (ConnectionClosed != null)
                            ConnectionClosed(this, null);
                        Worker.Abort();
                        return;
                    }
                }
                else
                {
                    var eventArgs = new IncomingPacketEventArgs(packet, true);
                    if (IncomingPacket != null)
                        IncomingPacket(this, eventArgs);
                    lock (Server)
                    {
                        if (!eventArgs.Handled)
                            ServerStream.WritePacket(packet, Craft.Net.PacketDirection.Serverbound);
                        // We use a BufferedStream to make sure packets get sent in one piece, rather than
                        // a field at a time. Flushing it here sends the assembled packet.
                        Server.Flush();
                    }
                    if (packet is DisconnectPacket)
                    {
                        Console.WriteLine("Client disconnected: " + ((DisconnectPacket)packet).Reason);
                        if (ConnectionClosed != null)
                            ConnectionClosed(this, null);
                        Worker.Abort();
                    }

                }
            }
        }

        private void UpdateServer()
        {
            while (Server.DataAvailable)
            {
                var packet = ServerStream.ReadPacket(Craft.Net.PacketDirection.Clientbound);
                Log.LogPacket(packet, false);

                if (packet is EncryptionKeyRequestPacket)
                    InitializeEncryption((EncryptionKeyRequestPacket)packet);
                else
                {
                    var eventArgs = new IncomingPacketEventArgs(packet, false);
                    if (IncomingPacket != null)
                        IncomingPacket(this, eventArgs);
                    lock (Client)
                    {
                        if (!eventArgs.Handled)
                            ClientStream.WritePacket(packet, Craft.Net.PacketDirection.Clientbound);
                        Client.Flush();
                    }
                    if (packet is DisconnectPacket)
                    {
                        Console.WriteLine("Server disconnected: " + ((DisconnectPacket)packet).Reason);
                        if (ConnectionClosed != null)
                            ConnectionClosed(this, null);
                        Worker.Abort();
                    }
                }
            }
        }

        private void InitializeEncryption(EncryptionKeyRequestPacket packet)
        {
            // We have to hijack the encryption here to be able to sniff the
            // connection. What we do is set up two unrelated crypto streams,
            // one for the server, one for the client. We actually act a bit
            // more like a real client or a real server in this particular
            // stage of the connection, because we generate a shared secret
            // as a client and a public key as a server, and liase with each
            // end of the connection without tipping them off to this. After
            // this is done, we wrap the connection in an AesStream and
            // everything works fine.

            // Interact with the server (acting as a client)

            // Generate our shared secret
            var secureRandom = RandomNumberGenerator.Create();
            ServerSharedKey = new byte[16];
            secureRandom.GetBytes(ServerSharedKey);

            // Parse the server public key
            var parser = new AsnKeyParser(packet.PublicKey);
            var key = parser.ParseRSAPublicKey();

            // Encrypt shared secret and verification token
            var crypto = new RSACryptoServiceProvider();
            crypto.ImportParameters(key);
            byte[] encryptedSharedSecret = crypto.Encrypt(ServerSharedKey, false);
            byte[] encryptedVerification = crypto.Encrypt(packet.VerificationToken, false);

            // Create an 0xFC response to give the server
            ServerEncryptionResponse = new EncryptionKeyResponsePacket
            {
                SharedSecret = encryptedSharedSecret,
                VerificationToken = encryptedVerification
            };

            // Authenticate with minecraft.net if need be
            if (packet.ServerId != "-")
            {
				try
				{
                	var session = Session.DoLogin(Settings.Username, Settings.Password);
                    // Generate session hash
                    byte[] hashData = Encoding.ASCII.GetBytes(packet.ServerId)
                        .Concat(ServerSharedKey)
                        .Concat(packet.PublicKey).ToArray();
                    var hash = Cryptography.JavaHexDigest(hashData);
                    var webClient = new WebClient();
                    string result = webClient.DownloadString("http://session.minecraft.net/game/joinserver.jsp?user=" +
                        Uri.EscapeUriString(session.UserName) +
                        "&sessionId=" + Uri.EscapeUriString(session.SessionId) +
                        "&serverId=" + Uri.EscapeUriString(hash));
                    if (result != "OK")
                        Console.WriteLine("Warning: Unable to login as user " + Settings.Username + ": " + result);
                    Console.WriteLine("(Session ID is {0})", session.SessionId);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Warning: Unable to login as user " + Settings.Username + ": " + e.Message);
                }
            }

            // Interact with the client (acting as a server)

            // Generate verification token
            ClientVerificationToken = new byte[4];
            secureRandom.GetBytes(ClientVerificationToken);
            // Encode public key as an ASN X509 certificate
            var encodedKey = AsnKeyBuilder.PublicKeyToX509(ServerKey);

            if (Settings.AuthenticateClients)
                ClientAuthenticationHash = CreateHash();
            else
                ClientAuthenticationHash = "-";

            ClientEncryptionRequest = new EncryptionKeyRequestPacket
            {
                VerificationToken = ClientVerificationToken,
                ServerId = ClientAuthenticationHash,
                PublicKey = encodedKey.GetBytes()
            };
            FinializeClientEncryption(new EncryptionKeyResponsePacket());
        }

        private bool FinializeClientEncryption(EncryptionKeyResponsePacket encryptionKeyResponsePacket)
        {
            // Here, we need to prepare everything to enable client<->proxy
            // encryption, but we can't turn it on quite yet.

            if (Settings.AuthenticateClients)
            {
                // Do authentication
                // Create a hash for session verification
                AsnKeyBuilder.AsnMessage encodedKey = AsnKeyBuilder.PublicKeyToX509(ServerKey);
                byte[] shaData = Encoding.UTF8.GetBytes(ClientAuthenticationHash)
                    .Concat(ClientSharedKey)
                    .Concat(encodedKey.GetBytes()).ToArray();
                string hash = Cryptography.JavaHexDigest(shaData);

                var client = new WebClient();
                var result = client.DownloadString(string.Format("http://session.minecraft.net/game/checkserver.jsp?user={0}&serverId={1}",
                    PlayerName, hash));
                if (result != "YES")
                {
                    Log.Write("Failed to authenticate " + PlayerName + "!");
                    ServerStream.WritePacket(new DisconnectPacket("Failed to authenticate!"), Craft.Net.PacketDirection.Serverbound);
                    ClientStream.WritePacket(new DisconnectPacket("Failed to authenticate!"), Craft.Net.PacketDirection.Clientbound);
                    Server.Flush();
                    Client.Flush();
                    return false;
                }
            }

            // Send unencrypted response
            ServerStream.WritePacket(ServerEncryptionResponse, Craft.Net.PacketDirection.Serverbound);
            Server.Flush();

            // We wait for the server to respond, then set up encryption
            // for both sides of the connection.
            ServerStream.BaseStream = new AesStream(Server, ServerSharedKey);
            return true;
        }

        private static string CreateHash()
        {
            byte[] hash = BitConverter.GetBytes(new Random().Next());
            string response = "";
            foreach (byte b in hash)
                response += b.ToString("x2");
            return response;
        }

        private void FinializeServerEncryption(EncryptionKeyResponsePacket encryptionKeyResponsePacket)
        {
            // Here, we have all the details we need to initialize our
            // proxy<->server crypto stream. This happens *after* we have
            // already completed the crypto handshake with the client.

            // Wrap the server stream in a crypto stream
            ServerStream = new NetworkManager(new AesStream(Server, ServerSharedKey));
            Log.Write("Encrypted server connection established.");

            // Write the response. This is the first encrypted packet
            // sent to the client. The correct response is to send
            // an 0xFC EncryptionKeyResponse with both fields as empty
            // arrays.
            var response = new EncryptionKeyResponsePacket
            {
                SharedSecret = new byte[0],
                VerificationToken = new byte[0]
            };
            ClientStream.WritePacket(response, Craft.Net.PacketDirection.Clientbound);
            Client.Flush();

            // Wrap the client stream in a crypto stream
            ClientStream = new NetworkManager(new AesStream(Client, ClientSharedKey));
            Log.Write("Encrypted client connection established.");

            // And now we're done with encryption and everything can
            // continue normally.
        }
    }
}
