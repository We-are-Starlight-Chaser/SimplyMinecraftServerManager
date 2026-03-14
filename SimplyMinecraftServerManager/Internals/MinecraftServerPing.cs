using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 用于获取Minecraft服务器状态的工具类
    /// </summary>
    public class MinecraftServerPing
    {
        public class ServerStatus
        {
            public string VersionName { get; set; } = "";
            public int ProtocolVersion { get; set; }
            public int MaxPlayers { get; set; }
            public int OnlinePlayers { get; set; }
            public List<Player> Players { get; set; } = new List<Player>();
            public string Motd { get; set; } = "";
            public string Icon { get; set; } = ""; // Base64 encoded icon
        }

        public class Player
        {
            public string Name { get; set; } = "";
            public string Id { get; set; } = "";
        }

        /// <summary>
        /// Ping指定的Minecraft服务器
        /// </summary>
        /// <param name="host">服务器地址</param>
        /// <param name="port">服务器端口</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>服务器状态信息</returns>
        public static ServerStatus? Ping(string host, int port = 25565, int timeout = 5000)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.ConnectAsync(host, port).Wait(timeout);
                    if (!result)
                    {
                        return null;
                    }

                    using (var stream = client.GetStream())
                    {
                        // 发送握手包
                        WriteVarInt(stream, 4); // Packet ID
                        WriteVarInt(stream, 754); // Protocol version (1.20.4)
                        WriteString(stream, host);
                        WriteShort(stream, (short)port);
                        WriteVarInt(stream, 1); // Status intent (1 for ping)

                        // 发送请求状态包
                        WriteVarInt(stream, 0); // Request packet ID

                        // 读取响应
                        int responseLength = ReadVarInt(stream);
                        int packetId = ReadVarInt(stream);

                        if (packetId != 0)
                        {
                            return null;
                        }

                        string json = ReadString(stream);
                        
                        // 使用 System.Text.Json 反序列化
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        var status = JsonSerializer.Deserialize<StatusResponse>(json, options);

                        if (status?.Players?.Sample != null)
                        {
                            foreach (var p in status.Players.Sample)
                            {
                                p.Id = p.Id.Replace("-", "");
                            }
                        }

                        // 发送ping包
                        WriteVarInt(stream, 1); // Packet ID
                        WriteLong(stream, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                        // 读取pong响应
                        int pongLength = ReadVarInt(stream);
                        int pongPacketId = ReadVarInt(stream);
                        long receivedTime = ReadLong(stream);

                        if (status != null)
                        {
                            return new ServerStatus
                            {
                                VersionName = status.Version?.Name ?? "",
                                ProtocolVersion = status.Version?.Protocol ?? 0,
                                MaxPlayers = status.Players?.Max ?? 0,
                                OnlinePlayers = status.Players?.Online ?? 0,
                                Players = status.Players?.Sample?.ConvertAll(p => new Player { Name = p.Name, Id = p.Id }) ?? new List<Player>(),
                                Motd = status.Description?.ToString() ?? "",
                                Icon = status.Favicon ?? ""
                            };
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 忽略连接错误
            }

            return null;
        }

        #region Helper Methods

        private static void WriteVarInt(Stream stream, int value)
        {
            while (true)
            {
                if ((value & 0xFFFFFF80) == 0)
                {
                    stream.WriteByte((byte)value);
                    return;
                }

                stream.WriteByte((byte)((value & 0xFF) | 0x80));
                value >>= 7;
            }
        }

        private static int ReadVarInt(Stream stream)
        {
            int value = 0;
            int size = 0;
            int b;

            while ((b = stream.ReadByte()) != -1)
            {
                value |= (b & 0x7F) << (size * 7);
                size++;

                if ((b & 0x80) == 0)
                    break;
            }

            return value;
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadString(Stream stream)
        {
            int length = ReadVarInt(stream);
            byte[] bytes = new byte[length];
            int totalRead = 0;

            while (totalRead < length)
            {
                int read = stream.Read(bytes, totalRead, length - totalRead);
                if (read == 0)
                    throw new EndOfStreamException();
                totalRead += read;
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static void WriteShort(Stream stream, short value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static short ReadShort(Stream stream)
        {
            return (short)((stream.ReadByte() << 8) | stream.ReadByte());
        }

        private static void WriteLong(Stream stream, long value)
        {
            stream.Write(BitConverter.GetBytes(value), 0, 8);
        }

        private static long ReadLong(Stream stream)
        {
            byte[] buffer = new byte[8];
            int totalRead = 0;

            while (totalRead < 8)
            {
                int read = stream.Read(buffer, totalRead, 8 - totalRead);
                if (read == 0)
                    throw new EndOfStreamException();
                totalRead += read;
            }

            return BitConverter.ToInt64(buffer, 0);
        }

        #endregion

        #region JSON Models

        public class StatusResponse
        {
            public StatusVersion? Version { get; set; }
            public StatusPlayers? Players { get; set; }
            public object? Description { get; set; } // Can be string or complex object
            public string? Favicon { get; set; }
        }

        public class StatusVersion
        {
            public string? Name { get; set; }
            public int Protocol { get; set; }
        }

        public class StatusPlayers
        {
            public int Max { get; set; }
            public int Online { get; set; }
            public List<StatusPlayer>? Sample { get; set; }
        }

        public class StatusPlayer
        {
            public string Name { get; set; } = "";
            public string Id { get; set; } = "";
        }

        #endregion
    }
}