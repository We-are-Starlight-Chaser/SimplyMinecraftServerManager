// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using DnsClient;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 用于获取Minecraft服务器状态的高性能纯异步工具类
    /// </summary>
    public static class MinecraftServerPing
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        #region Public Models

        public class ServerStatus
        {
            public string VersionName { get; set; } = "";
            public int ProtocolVersion { get; set; }
            public int MaxPlayers { get; set; }
            public int OnlinePlayers { get; set; }
            public List<Player> Players { get; set; } = [];
            public string Motd { get; set; } = "";
            public string Icon { get; set; } = "";
            public long LatencyMs { get; set; }
        }

        public class Player
        {
            public string Name { get; set; } = "";
            public string Id { get; set; } = "";
        }

        #endregion

        #region Raw JSON Models (Internal)

        private class StatusResponse
        {
            public StatusVersion? Version { get; set; }
            public StatusPlayers? Players { get; set; }
            public JsonElement? Description { get; set; }
            public string? Favicon { get; set; }
        }

        private class StatusVersion
        {
            public string? Name { get; set; }
            public int Protocol { get; set; }
        }

        private class StatusPlayers
        {
            public int Max { get; set; }
            public int Online { get; set; }
            public List<StatusPlayer>? Sample { get; set; }
        }

        private class StatusPlayer
        {
            public string? Name { get; set; }
            public string? Id { get; set; }
        }

        #endregion

        /// <summary>
        /// Ping指定的Minecraft服务器
        /// </summary>
        public static async Task<ServerStatus?> PingAsync(
            string host, int port = 25565, int protocolVersion = 0,
            int timeoutMs = 5000, CancellationToken ct = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);
            var token = linkedCts.Token;

            try
            {
                if (port == 25565)
                {
                    try
                    {
                        var srvResult = await ResolveSrvAsync(host, token);
                        if (srvResult != null)
                        {
                            Debug.WriteLine($"[McPinger] SRV resolved: {host} → {srvResult.Value.Host}:{srvResult.Value.Port}");
                            host = srvResult.Value.Host;
                            port = srvResult.Value.Port;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Debug.WriteLine($"[McPinger] SRV lookup failed (non-fatal): {ex.Message}");
                    }
                }

                IPAddress? address;
                if (IPAddress.TryParse(host, out var parsed))
                {
                    address = parsed;
                    Debug.WriteLine($"[McPinger] IP literal: {host} (DNS skipped)");
                }
                else
                {
                    var addresses = await Dns.GetHostAddressesAsync(host, token);
                    // 优先 IPv4，回退 IPv6
                    address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                           ?? addresses.FirstOrDefault()
                           ?? throw new Exception($"No address resolved for '{host}'");
                    Debug.WriteLine($"[McPinger] DNS resolved: {host} → {address}");
                }

                Debug.WriteLine($"[McPinger] Connecting to {address}:{port}...");
                using var client = new TcpClient { NoDelay = true };
                await client.ConnectAsync(address, port, token);
                Debug.WriteLine($"[McPinger] TCP connected to {address}:{port}");

                var pipe = new Pipe();
                var stream = client.GetStream();

                await WriteHandshakeAndRequestAsync(stream, host, port, protocolVersion, token)
                    .ConfigureAwait(false);

                // 写入完成后才启动 socket→pipe 拷贝
                var socketReading = CopySocketToPipeAsync(stream, pipe.Writer, token);

                var status = await ReadAndParseResponseAsync(pipe.Reader, stream, token)
                    .ConfigureAwait(false);

                // 等待后台读取任务结束（无论正常还是取消）
                try { await socketReading.ConfigureAwait(false); }
                catch { /* 忽略后台读取的取消/断开异常 */ }

                return status;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Debug.WriteLine($"[McPinger] ✗ Ping {host}:{port} timed out after {timeoutMs}ms");
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"[McPinger] ✗ Ping {host}:{port} failed: {ex}");
                return null;
            }
        }

        private static async Task CopySocketToPipeAsync(
            NetworkStream stream, PipeWriter writer, CancellationToken ct)
        {
            try
            {
                long totalBytes = 0;
                while (!ct.IsCancellationRequested)
                {
                    var memory = writer.GetMemory(4096);
                    var bytesRead = await stream.ReadAsync(memory, ct).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        Debug.WriteLine($"[McPinger] ← Socket closed (total {totalBytes} bytes read)");
                        break;
                    }
                    totalBytes += bytesRead;
                    writer.Advance(bytesRead);

                    var result = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (result.IsCompleted || result.IsCanceled) break;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[McPinger] ← SocketToPipe cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[McPinger] ← SocketToPipe error: {ex.Message}");
            }
            finally
            {
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        // ==================== 写入层 ====================

        private static async ValueTask WriteHandshakeAndRequestAsync(
            NetworkStream stream, string host, int port, int protocolVersion, CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                const int PrefixMaxSize = 5;
                int bodyStart = PrefixMaxSize;
                int offset = bodyStart;

                // Handshake body
                offset += WriteVarInt(buffer.AsSpan(offset), 0x00);
                offset += WriteVarInt(buffer.AsSpan(offset), protocolVersion);
                offset += WriteString(buffer.AsSpan(offset), host);
                BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)port);
                offset += 2;
                offset += WriteVarInt(buffer.AsSpan(offset), 1); // Next state: Status

                int bodyLen = offset - bodyStart;
                int prefixLen = WriteVarInt(buffer, bodyLen);

                int shift = bodyStart - prefixLen;
                if (shift > 0)
                    Buffer.BlockCopy(buffer, bodyStart, buffer, prefixLen, bodyLen);

                int handshakeTotalLen = prefixLen + bodyLen;

                // Status Request 紧跟其后
                buffer[handshakeTotalLen] = 1;     // Length prefix
                buffer[handshakeTotalLen + 1] = 0; // Packet ID

                int totalLen = handshakeTotalLen + 2;
                Debug.WriteLine($"[McPinger] → Handshake+StatusReq ({totalLen} bytes)");

                await stream.WriteAsync(buffer.AsMemory(0, totalLen), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async ValueTask SendPingRequestAsync(
            NetworkStream stream, long timestamp, CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16);
            try
            {
                const int PrefixMaxSize = 5;
                int bodyStart = PrefixMaxSize;
                int offset = bodyStart;

                offset += WriteVarInt(buffer.AsSpan(offset), 0x01);
                BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset), timestamp);
                offset += 8;

                int bodyLen = offset - bodyStart;
                int prefixLen = WriteVarInt(buffer, bodyLen);

                int shift = bodyStart - prefixLen;
                if (shift > 0)
                    Buffer.BlockCopy(buffer, bodyStart, buffer, prefixLen, bodyLen);

                int totalLen = prefixLen + bodyLen;
                Debug.WriteLine($"[McPinger] → Ping ({totalLen} bytes)");

                await stream.WriteAsync(buffer.AsMemory(0, totalLen), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // ==================== 读取层 ====================

        private static async Task<ServerStatus?> ReadAndParseResponseAsync(
            PipeReader reader, NetworkStream stream, CancellationToken ct)
        {
            ServerStatus? status = null;
            bool pingSent = false;
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryParsePacket(ref buffer, out var packetId, out var payload))
                {
                    Debug.WriteLine($"[McPinger] ← Received packet 0x{packetId:X2} ({payload.Length} bytes)");

                    switch (packetId)
                    {
                        case 0x00 when status == null:
                            status = ParseStatusJson(payload);
                            if (status != null && !pingSent)
                            {
                                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                Debug.WriteLine($"[McPinger] Status parsed, sending Ping (ts={ts})...");
                                await SendPingRequestAsync(stream, ts, ct).ConfigureAwait(false);
                                stopwatch.Restart();
                                pingSent = true;
                            }
                            break;

                        case 0x01 when payload.Length >= 8:
                            var pongReader = new SequenceReader<byte>(payload);
                            if (pongReader.TryReadBigEndian(out long _))
                            {
                                status ??= new ServerStatus();
                                status.LatencyMs = stopwatch.ElapsedMilliseconds;
                            }
                            Debug.WriteLine($"[McPinger] ← Pong received, latency={status?.LatencyMs}ms");

                            reader.AdvanceTo(buffer.Start, buffer.End);
                            await reader.CompleteAsync().ConfigureAwait(false);
                            return status;
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted || result.IsCanceled)
                {
                    Debug.WriteLine($"[McPinger] Pipe completed/cancelled (status={(status != null ? "parsed" : "null")})");
                    break;
                }
            }

            await reader.CompleteAsync().ConfigureAwait(false);
            return status;
        }

        private static bool TryParsePacket(
            ref ReadOnlySequence<byte> buffer,
            out int packetId,
            out ReadOnlySequence<byte> payload)
        {
            packetId = 0;
            payload = default;

            var probe = new SequenceReader<byte>(buffer);

            // 1. 读取包体总长度 (VarInt)
            if (!TryReadVarInt(ref probe, out int packetLength))
                return false;

            // 防御性检查
            if (packetLength <= 0 || packetLength > 2 * 1024 * 1024)
            {
                Debug.WriteLine($"[McPinger] Invalid packetLength={packetLength}");
                return false;
            }

            // 2. 检查剩余数据是否足够
            if (probe.Remaining < packetLength)
                return false;

            // 3. 记录包体起始位置（相对于 buffer 开头）
            long bodyStartPos = probe.Consumed;

            // 4. 读取 Packet ID (VarInt)
            if (!TryReadVarInt(ref probe, out packetId))
                return false;

            // 5. 计算载荷长度
            long idBytesConsumed = probe.Consumed - bodyStartPos;
            int payloadLength = (int)(packetLength - idBytesConsumed);

            if (payloadLength < 0)
            {
                Debug.WriteLine($"[McPinger] Negative payloadLength! packetLength={packetLength}, idBytes={idBytesConsumed}");
                return false;
            }

            long payloadStartPos = bodyStartPos + idBytesConsumed;
            payload = buffer.Slice(payloadStartPos, payloadLength);

            long totalConsumed = probe.Consumed + payloadLength;
            buffer = buffer.Slice(totalConsumed);

            return true;
        }

        // ==================== JSON 解析与 MOTD 提取 ====================

        private static ServerStatus? ParseStatusJson(ReadOnlySequence<byte> jsonPayload)
        {
            try
            {
                if (jsonPayload.Length == 0)
                {
                    Debug.WriteLine("[McPinger] Empty JSON payload received");
                    return null;
                }

                // MC String = [VarInt length][UTF-8 bytes]
                var strReader = new SequenceReader<byte>(jsonPayload);
                if (!TryReadVarInt(ref strReader, out int jsonLength) || jsonLength <= 0)
                {
                    Debug.WriteLine($"[McPinger] Failed to read MC String VarInt prefix");
                    return null;
                }

                var jsonBytes = jsonPayload.Slice(strReader.Consumed, jsonLength);
                string json = jsonBytes.IsSingleSegment
                    ? Encoding.UTF8.GetString(jsonBytes.FirstSpan)
                    : Encoding.UTF8.GetString(jsonBytes.ToArray());
                Debug.WriteLine($"[SLP Raw] {json}");
                var raw = JsonSerializer.Deserialize<StatusResponse>(json, JsonOptions);
                if (raw == null) return null;

                // Disconnect 包检测
                if (raw.Version == null && raw.Players == null)
                {
                    string reason = ExtractPlainText(raw.Description) ?? json;
                    Debug.WriteLine($"[McPinger] ✗ Server rejected: {reason}");
                    return null;
                }

                var status = new ServerStatus
                {
                    VersionName = raw.Version?.Name ?? "",
                    ProtocolVersion = raw.Version?.Protocol ?? 0,
                    MaxPlayers = raw.Players?.Max ?? 0,
                    OnlinePlayers = raw.Players?.Online ?? 0,
                    Icon = raw.Favicon ?? "",
                    Motd = raw.Description?.GetRawText() ?? ""
                };

                if (raw.Players?.Sample != null)
                {
                    foreach (var p in raw.Players.Sample)
                    {
                        status.Players.Add(new Player
                        {
                            Name = p.Name ?? "",
                            Id = p.Id ?? ""
                        });
                    }
                }

                return status;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[McPinger] JSON parse error: {ex.Message}");
                return null;
            }
        }
        private static string? ExtractPlainText(JsonElement? description)
        {
            if (!description.HasValue) return null;
            var desc = description.Value;

            if (desc.ValueKind == JsonValueKind.String)
                return desc.GetString();

            if (desc.ValueKind != JsonValueKind.Object)
                return desc.ToString();

            var sb = new StringBuilder();

            // 提取 "text" 字段
            if (desc.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                sb.Append(textProp.GetString());

            // 递归提取 "extra" 数组
            if (desc.TryGetProperty("extra", out var extraProp) && extraProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in extraProp.EnumerateArray())
                {
                    var childText = ExtractPlainText(item);
                    if (childText != null) sb.Append(childText);
                }
            }

            // 处理 translate 组件
            if (sb.Length == 0 && desc.TryGetProperty("translate", out var translateProp))
                sb.Append(translateProp.GetString() ?? "");

            return sb.Length > 0 ? sb.ToString() : null;
        }

        // ==================== DNS / SRV ====================

        private static readonly LookupClient _lookupClient = new();
        private static async Task<(string Host, int Port)?> ResolveSrvAsync(
            string domain, CancellationToken ct = default)
        {
            var result = await _lookupClient.QueryAsync(
                $"_minecraft._tcp.{domain}", QueryType.SRV, cancellationToken: ct);

            var record = result.Answers.SrvRecords().FirstOrDefault();
            if (record == null) return null;

            return (record.Target.Value.TrimEnd('.'), record.Port);
        }

        #region Zero-Allocation Protocol Primitives

        public static int WriteVarInt(Span<byte> destination, int value)
        {
            int i = 0;
            uint v = (uint)value;
            while (v >= 0x80)
            {
                destination[i++] = (byte)(v | 0x80);
                v >>= 7;
            }
            destination[i++] = (byte)v;
            return i;
        }

        public static int WriteString(Span<byte> destination, string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            int varIntLen = WriteVarInt(destination, byteCount);

            if (varIntLen + byteCount > destination.Length)
                throw new ArgumentException(
                    $"String '{value}' requires {varIntLen + byteCount} bytes but destination has {destination.Length}");

            Encoding.UTF8.GetBytes(value, destination[varIntLen..]);
            return varIntLen + byteCount;
        }

        public static bool TryReadVarInt(ref SequenceReader<byte> reader, out int value)
        {
            value = 0;
            int shift = 0;
            while (true)
            {
                if (!reader.TryRead(out byte b)) return false;
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
                shift += 7;
                if (shift > 35) throw new InvalidDataException("VarInt is too big");
            }
        }

        #endregion
    }
}