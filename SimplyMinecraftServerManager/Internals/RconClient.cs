// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// RCON 连接配置信息，包含主机、端口和密码。
    /// </summary>
    public sealed class RconConnectionInfo
    {
        /// <summary>
        /// 获取或初始化 RCON 服务器主机地址。
        /// </summary>
        public string Host { get; init; } = "127.0.0.1";

        /// <summary>
        /// 获取或初始化 RCON 服务器端口号。
        /// </summary>
        public int Port { get; init; }

        /// <summary>
        /// 获取或初始化 RCON 认证密码。
        /// </summary>
        public string Password { get; init; } = "";
    }

    /// <summary>
    /// RCON 客户端，用于与 Minecraft 服务器进行远程控制台通信。
    /// </summary>
    /// <param name="connectionInfo">RCON 连接配置信息。</param>
    internal sealed class RconClient(RconConnectionInfo connectionInfo) : IAsyncDisposable
    {
        private readonly string _host = connectionInfo.Host;
        private readonly int _port = connectionInfo.Port;
        private readonly string _password = connectionInfo.Password;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private int _requestId = 1;

        /// <summary>
        /// 异步执行 RCON 命令并返回响应结果。
        /// </summary>
        /// <param name="command">要执行的命令。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>命令执行后的响应字符串。</returns>
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

                // 用短暂等待替代 DataAvailable 检查，避免 TCP 包未到达 kernel buffer 时的竞态
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                waitCts.CancelAfter(TimeSpan.FromMilliseconds(50));
                try
                {
                    var nextPacket = await ReadPacketAsync(waitCts.Token);
                    if (nextPacket.RequestId == requestId)
                    {
                        if (!string.IsNullOrEmpty(nextPacket.Payload))
                            responses.Add(nextPacket.Payload);
                        continue;
                    }
                }
                catch (OperationCanceledException) { }
                break;
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
            using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            authCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                while (true)
                {
                    var packet = await ReadPacketAsync(authCts.Token);
                    if (packet.Type == 2)
                    {
                        authenticated = packet.RequestId == requestId;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }

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

        private readonly byte[] _lengthBuffer = new byte[4];

        private async Task<RconPacket> ReadPacketAsync(CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("RCON 连接未建立。");
            }

            await ReadExactAsync(_stream, _lengthBuffer, cancellationToken);
            int length = BinaryPrimitives.ReadInt32LittleEndian(_lengthBuffer);

            if (length < 10 || length > 1024 * 1024)
            {
                throw new InvalidOperationException("收到无效的 RCON 响应。");
            }

            var bodyBuffer = new byte[length];
            await ReadExactAsync(_stream, bodyBuffer, cancellationToken);

            int requestId = BinaryPrimitives.ReadInt32LittleEndian(bodyBuffer);
            int type = BinaryPrimitives.ReadInt32LittleEndian(bodyBuffer.AsSpan(4));
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
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), value);
        }

        private int GetNextRequestId()
        {
            return Interlocked.Increment(ref _requestId);
        }

        private static string NormalizeCommand(string command)
        {
            return command.Trim().TrimStart('/');
        }

        /// <summary>
        /// 异步释放 RCON 客户端资源。
        /// </summary>
        /// <returns>表示异步操作完成的 ValueTask。</returns>
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
