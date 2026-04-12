using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    public sealed class RconConnectionInfo
    {
        public string Host { get; init; } = "127.0.0.1";
        public int Port { get; init; }
        public string Password { get; init; } = "";
    }

    internal sealed class RconClient : IAsyncDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _password;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private int _requestId = 1;

        public RconClient(RconConnectionInfo connectionInfo)
        {
            _host = connectionInfo.Host;
            _port = connectionInfo.Port;
            _password = connectionInfo.Password;
        }

        public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return string.Empty;
            }

            await ConnectAndAuthenticateAsync(cancellationToken);

            int requestId = GetNextRequestId();
            await SendPacketAsync(requestId, 2, NormalizeCommand(command), cancellationToken);

            var responses = new List<string>();
            while (true)
            {
                var packet = await ReadPacketAsync(cancellationToken);
                if (packet.RequestId != requestId)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(packet.Payload))
                {
                    responses.Add(packet.Payload);
                }

                if (_stream == null || !_stream.DataAvailable)
                {
                    break;
                }
            }

            return string.Join(Environment.NewLine, responses)
                .Replace("\0", string.Empty)
                .Trim();
        }

        private async Task ConnectAndAuthenticateAsync(CancellationToken cancellationToken)
        {
            if (_client != null && _client.Connected && _stream != null)
            {
                return;
            }

            _client?.Dispose();
            _client = new TcpClient
            {
                NoDelay = true,
                ReceiveTimeout = 4000,
                SendTimeout = 4000
            };

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(4));
            await _client.ConnectAsync(_host, _port, connectCts.Token);
            _stream = _client.GetStream();

            int requestId = GetNextRequestId();
            await SendPacketAsync(requestId, 3, _password, cancellationToken);

            bool authenticated = false;
            while (true)
            {
                var packet = await ReadPacketAsync(cancellationToken);
                if (packet.Type == 2)
                {
                    authenticated = packet.RequestId == requestId;
                    break;
                }
            }

            if (!authenticated)
            {
                throw new InvalidOperationException("RCON 认证失败。");
            }
        }

        private async Task SendPacketAsync(int requestId, int type, string payload, CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("RCON 连接未建立。");
            }

            var bodyBytes = Encoding.UTF8.GetBytes(payload);
            int length = 4 + 4 + bodyBytes.Length + 2;
            var buffer = new byte[4 + length];

            WriteInt32(buffer, 0, length);
            WriteInt32(buffer, 4, requestId);
            WriteInt32(buffer, 8, type);
            bodyBytes.CopyTo(buffer, 12);
            buffer[12 + bodyBytes.Length] = 0;
            buffer[13 + bodyBytes.Length] = 0;

            await _stream.WriteAsync(buffer, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        private async Task<RconPacket> ReadPacketAsync(CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("RCON 连接未建立。");
            }

            var lengthBuffer = new byte[4];
            await ReadExactAsync(_stream, lengthBuffer, cancellationToken);
            int length = BitConverter.ToInt32(lengthBuffer, 0);

            if (length < 10 || length > 1024 * 1024)
            {
                throw new InvalidOperationException("收到无效的 RCON 响应。");
            }

            var bodyBuffer = new byte[length];
            await ReadExactAsync(_stream, bodyBuffer, cancellationToken);

            int requestId = BitConverter.ToInt32(bodyBuffer, 0);
            int type = BitConverter.ToInt32(bodyBuffer, 4);
            string payload = Encoding.UTF8.GetString(bodyBuffer, 8, Math.Max(0, length - 10));

            return new RconPacket(requestId, type, payload);
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
                if (read <= 0)
                {
                    throw new IOException("RCON 连接已关闭。");
                }

                offset += read;
            }
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
        }

        private int GetNextRequestId()
        {
            return Interlocked.Increment(ref _requestId);
        }

        private static string NormalizeCommand(string command)
        {
            return command.Trim().TrimStart('/');
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _stream?.Dispose();
            }
            catch
            {
            }

            try
            {
                _client?.Dispose();
            }
            catch
            {
            }

            _stream = null;
            _client = null;
            return ValueTask.CompletedTask;
        }

        private readonly record struct RconPacket(int RequestId, int Type, string Payload);
    }
}
