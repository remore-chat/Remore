﻿using NetCoreServer;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Remore.Library.Packets;
using Remore.Library.Packets.Client;
using Remore.Library.Packets.Server;
using Remore.Server;
using Remore.Server.EF;
using Remore.Server.Services;
using Remore.Library;

public class RemoreServer
{

    public IPAddress Ip { get; set; }
    public int Port { get; set; }
    public string Version => "1.0.0";
    public List<Channel> Channels { get; set; }
    public List<ServerSession> Clients { get; set; }
    public TCPServer TCP { get; }
    public UDPServer UDP { get; set; }

    public string Name => Configuration.Name;
    public int MaxClients => Configuration.MaxClients;
    public ServerConfiguration Configuration { get; set; }

    public ConfigurationService ConfigurationService { get; private set; }

    public ServerDbContext Context { get; }

    private static readonly bool isVoiceDebugModeEnabled = Environment.GetCommandLineArgs().Contains("--voice-debug");
    public void Start()
    {
        if (isVoiceDebugModeEnabled)
            Logger.LogInfo($"Voice debug mode enabled");

        Task.Run(async () =>
        {
            Configuration = await ConfigurationService.GetServerConfigurationAsync();
        }).GetAwaiter().GetResult();
        TCP.Start();
        UDP.Start();
    }


    public void Stop()
    {
        TCP.Multicast(new DisconnectPacket() { Reason = "Server stopped." });
        TCP.Stop();
        UDP.Stop();
    }

    public RemoreServer(IPAddress ip, int port)
    {
        Ip = ip;
        Port = port;
        Clients = new();
        ConfigurationService = ServiceContainer.GetService<ConfigurationService>();
        Context = ServiceContainer.GetService<ServerDbContext>();
        Channels = Context.Channels.ToList();
        foreach (var channel in Channels)
        {
            if (channel.Messages == null)
                channel.Messages = new();
            channel.Messages.AddRange(Context.ChannelMessages.Where(x => x.ChannelId == channel.Id).OrderByDescending(x => x.CreatedAt).Take(20).ToList());
        }
        TCP = new(this, ip, port);
        UDP = new(this, IPAddress.Any, port);

    }

    public class UDPServer : UdpServer
    {
        public UDPServer(RemoreServer server, IPAddress address, int port) : base(address, port)
        {
            Server = server;
            Clients = new();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    foreach (var client in Clients.ToList())
                    {
                        if (DateTimeOffset.Now.ToUnixTimeSeconds() - client.HeartbeatReceivedAt > 10)
                        {
                            Clients.Remove(client);
                            var channel = Server.Channels.FirstOrDefault(x => x.ConnectedClients.Any(x => x == client));
                            if (channel != null)
                                channel.ConnectedClients.Remove(client);
                            continue;
                        }
                        this.Send(client.EndPoint, new PacketWriter(new UdpHeartbeatPacket() { ClientUsername = client.Username }).Write());
                    }
                    await Task.Delay(10 * 1000);
                }
            });
        }

        public List<UdpSession> Clients { get; }
        public RemoreServer Server { get; }

        protected override void OnStarted()
        {
            ReceiveAsync();
        }

        protected override async void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {
            var packet = Packet.FromByteArray(buffer);
            var client = Clients.FirstOrDefault(x => x.EndPoint.ToString() == endpoint.ToString());
            if (client == null)
            {
                if (packet is UdpAuthenticationPacket udpAuthentication)
                {
                    var tcpSession = Server.Clients.FirstOrDefault(x => x.Id.ToString() == udpAuthentication.TcpId);
                    if (tcpSession != null && tcpSession.Username == udpAuthentication.ClientUsername)
                    {

                        var session = new UdpSession()
                        {
                            EndPoint = endpoint,
                            HeartbeatReceivedAt = DateTimeOffset.Now.ToUnixTimeSeconds(),
                            Server = this,
                            TcpSession = tcpSession,
                            Username = udpAuthentication.ClientUsername,
                        };
                        Clients.Add(session);
                        Logger.LogInfo($"New UDP client {tcpSession.Id} ({udpAuthentication.ClientUsername}) connected");
                        this.Send(endpoint, new UdpNotifyConnectedPacket() { ClientUsername = udpAuthentication.ClientUsername });
                    }
                }
            }
            else
            {
                if (packet is UdpAuthenticationPacket udpAuthentication)
                {
                    Clients.Remove(client);
                    Logger.LogInfo($"Reconnecting client {udpAuthentication.ClientUsername}");
                    var tcpSession = Server.Clients.FirstOrDefault(x => x.Id.ToString() == udpAuthentication.TcpId);
                    if (tcpSession != null && tcpSession.Username == udpAuthentication.ClientUsername)
                    {

                        var session = new UdpSession()
                        {
                            EndPoint = endpoint,
                            HeartbeatReceivedAt = DateTimeOffset.Now.ToUnixTimeSeconds(),
                            Server = this,
                            TcpSession = tcpSession,
                            Username = udpAuthentication.ClientUsername,
                        };
                        Clients.Add(session);
                        Logger.LogInfo($"New UDP client {tcpSession.Id} ({udpAuthentication.ClientUsername}) connected");
                        this.Send(endpoint, new UdpNotifyConnectedPacket() { ClientUsername = udpAuthentication.ClientUsername });
                        Logger.LogInfo($"Reconnected client {udpAuthentication.ClientUsername} {endpoint}");
                    }
                }
                else if (packet is UdpHeartbeatPacket heartbeat)
                {
                    if (ValidateClient(endpoint, heartbeat.ClientUsername, out var session))
                    {
                        //Logger.LogInfo($"Heartbeat {session.TcpSession.Id} ({session.Username})");
                        session.HeartbeatReceivedAt = DateTimeOffset.Now.ToUnixTimeSeconds();

                    }
                }
                else if (packet is VoiceDataPacket voiceData)
                {

                    if (ValidateClient(endpoint, voiceData.ClientUsername, out var session))
                    {
                        var tcp = session.TcpSession;
                        if (tcp.CurrentChannel == null)
                        {
                            //Logger.LogWarn("Got invalid voice data packet");
                        }
                        else
                        {
                            Parallel.ForEach(tcp.CurrentChannel.ConnectedClients.ToList(), (vClient) =>
                              {
                                  //Do not send voice data back to sender unless voice debug mode enabled
                                  if (!isVoiceDebugModeEnabled && vClient.Username == session.Username)
                                      return;
                                  var actualSent = this.Send(vClient.EndPoint, new VoiceDataMulticastPacket() { Username = session.Username, VoiceData = voiceData.VoiceData });
                              });
                        }
                    }
                }
                else if (packet is UdpDisconnectPacket disconnectPacket)
                {
                    if (ValidateClient(endpoint, disconnectPacket.ClientUsername, out var session))
                    {
                        var currentChannel = session.TcpSession.CurrentChannel;
                        if (currentChannel != null)
                        {
                            if (currentChannel.ConnectedClients.Remove(session))
                                this.Server.TCP.Multicast(new ChannelUserDisconnected() { ChannelId = currentChannel.Id, Username = session.Username });
                        }
                        session.TcpSession.CurrentChannel = null;

                        Clients.Remove(session);
                    }
                }
            }
            ReceiveAsync();
        }


        public bool ValidateClient(EndPoint endpoint, string username, out UdpSession client)
        {
            client = Clients.FirstOrDefault(x => x.EndPoint.ToString() == endpoint.ToString() && x.Username == username);
            return client != null;
        }

    }

    public class TCPServer : TcpServer
    {

        public TCPServer(RemoreServer server, IPAddress address, int port) : base(address, port)
        {
            Server = server;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        foreach (var session in server.Clients.ToList())
                        {
                            if (!session.IsNegotationCompleted)
                                continue;

                            if (DateTimeOffset.Now.ToUnixTimeSeconds() - session.LatestHeartbeatReceivedAt > 15 && session.State == SessionState.Connected)
                            {
                                session.Send(new DisconnectPacket("Server haven't received heartbeat from your client more than 15 seconds"));
                                session.Disconnect();
                                continue;
                            }
                            session.Send(new TcpHeartbeatPacket());
                            Logger.LogInfo($"Sent heartbeat to {session.Username}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ;
                    }
                    await Task.Delay(10 * 1000);
                }
            });
        }

        public RemoreServer Server { get; }

        protected override TcpSession CreateSession()
        {
            return new ServerSession(Server);
        }

        protected override void OnError(SocketError error)
        {
            Logger.LogError($"TCP server caught an error with code {error}");
        }
    }
}