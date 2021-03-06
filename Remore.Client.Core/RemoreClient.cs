using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Remore.Client.Core.Utility;
using Remore.Library.Packets.Client;
using Remore.Library.Packets.Server;
using Remore.Library.Packets;
using Remore.Client.Core.Exceptions;

namespace Remore.Client.Core
{
    public partial class RemoreClient
    {
        private const string version = "1.0.0";

        /// <summary>
        /// Initializes new Remore Client
        /// </summary>
        /// <param name="ip">IP address of ther server</param>
        /// <param name="port">Port of the server</param>
        /// <param name="username">Username that will be used on server</param>
        /// <param name="password">User server password (currently unimplemented)</param>
        public RemoreClient(string ip, int port, string username, string? privilegeKey = null, string? password = null)
        {
            PacketReader.Init();
            Ip = ip;
            Port = port;
            Version = version;
            Username = username;
            PrivilegeKey = privilegeKey;
            _tcpClient = new RemoreTcpClient(Ip, Port, Username, privilegeKey);
        }

        ///// <summary>
        ///// Initializes new Remore bot client (currently unavailable)
        ///// </summary>
        ///// <param name="ip">IP address of ther server</param>
        ///// <param name="port">Port of the server</param>
        ///// <param name="botToken">Token of the bot generated on server</param>
        //public RemoreClient(string ip, int port, string botToken)
        //{
        //    Ip = ip;
        //    Port = port;
        //    BotToken = botToken;
        //    Version = $"BOT-{version}";
        //}


        public string Version { get; }
        public string Username { get; }
        public string? PrivilegeKey { get; }
        public string Ip { get; }
        public int Port { get; }
        public bool IsBot => BotToken != null;
        public string BotToken { get; }

        public event EventHandler<IPacket> PacketReceived;
        public event EventHandler<IPacket> UDPPacketReceived;
        public event EventHandler<object> Ready;

        private RemoreTcpClient _tcpClient;
        private RemoreUdpClient _udpClient;

        public bool IsConnected => _tcpClient.IsConnected;

        private PendingDictionary<string, IPacket> _pending = new();

        /// <summary>
        /// Connects client to specified server
        /// </summary>
        /// <param name="connectionTimeout">Sets connection timeout</param>
        /// <exception cref="ConnectionFailedException"></exception>
        public async Task ConnectAsync(int connectionTimeout = 60000)
        {
            await InternalTcpConnectAsync(connectionTimeout);
            await InternalUdpConnect(connectionTimeout);
            Ready?.Invoke(this, null);
        }
        public async void Disconnect()
        {
            _tcpClient.DisconnectAndStop();
            _udpClient.Send(new UdpDisconnectPacket() { ClientUsername = Username });
            _udpClient?.DisconnectAndStop();
        }
        public async Task SendPacketTCP(IPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (!_tcpClient.IsConnected && _tcpClient.State != SessionState.Connected)
                throw new Exception("Can't send packets if state is not connected");

            _tcpClient.Send(packet);
        }

        public async Task SendPacketUDP(IUdpPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (!_udpClient.IsConnectedToServer)
                throw new Exception("Can't send packets if state is not connected");
            _udpClient.Send(packet as IPacket);
        }

        #region Packet sending methods implementation

        public async Task<RequestChannelJoinResponse?> RequestChannelJoinAsync(string channelId)
        {
            var packet = new RequestChannelJoin()
            {
                ChannelId = channelId,
                RequestId = Guid.NewGuid().ToString()
            };
            await SendPacketTCP(packet);
            return await _pending.WaitForValueAsync(packet.RequestId, 5000) as RequestChannelJoinResponse;
        }
        public async Task<ChannelMessagesResponse?> RequestChannelMessages(string channelId, int page)
        {
            var packet = new RequestChannelMessagesPacket()
            {
                ChannelId = channelId,
                RequestId = Guid.NewGuid().ToString(),
                Page = page
            };
            await SendPacketTCP(packet);
            return await _pending.WaitForValueAsync(packet.RequestId, 15000) as ChannelMessagesResponse;
        }
        public async Task UdpReconnectAsync()
        {
            await InternalUdpConnect();
        }
        #endregion


        private async Task InternalTcpConnectAsync(int connectionTimeout = 5000)
        {
            _tcpClient.PacketReceived += OnTcpPacketReceived;
            var tcpSuccess = _tcpClient.Connect();
            if (!tcpSuccess)
            {
                throw new ConnectionFailedException(SocketType.Stream, "Failed to connect to server");
            }
            async Task TcpIdWaiter()
            {
                while (_tcpClient.TcpId == null)
                {
                    await Task.Delay(200);
                }
            }
            await Task.WhenAny(TcpIdWaiter(), Task.Delay(connectionTimeout));
            var tcpId = _tcpClient.TcpId;
            if (tcpId == null)
                throw new ConnectionFailedException(SocketType.Stream, $"Failed to connect to server. Client didn't receive needed information in {connectionTimeout / 1000} seconds after connecting");
        }


        private async Task InternalUdpConnect(int connectionTimeout = 5000)
        {
            _udpClient = new RemoreUdpClient(_tcpClient.TcpId, IPAddress.Parse(Ip), Port, Username);
            _udpClient.Connect();
            async Task ConnectionWaiter()
            {
                while (!_udpClient.IsConnectedToServer)
                    await Task.Delay(200);
            }
            await Task.WhenAny(ConnectionWaiter(), Task.Delay(connectionTimeout));
            if (!_udpClient.IsConnectedToServer)
                throw new ConnectionFailedException(SocketType.Dgram, "Udp client failed to connect. Timed out");
            _udpClient.PacketReceived += OnUdpPacketReceived;
        }

        private void OnUdpPacketReceived(object? sender, IPacket e)
        {
            UDPPacketReceived?.Invoke(this, e);
        }

        private void OnTcpPacketReceived(object? sender, IPacket packet)
        {
            if (Ip == "127.0.0.1")
                Thread.Sleep(5);
            if (!string.IsNullOrWhiteSpace(packet.RequestId) && _pending.Add(packet.RequestId, packet))
                return;

            PacketReceived?.Invoke(this, packet);
        }
    }
}
