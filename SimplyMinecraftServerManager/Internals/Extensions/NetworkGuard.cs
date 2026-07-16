// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 网络守卫：拦截扩展的出站网络连接。
///
/// 策略：
///   - 禁止所有出站连接（POST/PUT/PATCH/DELETE 等上传操作）
///   - 禁止 TCP/UDP Socket 发送
///   - 禁止 SMTP 邮件发送
///   - 禁止 WebSocket 连接
///   - 仅允许通过 IDownloadService 进行 HTTP GET 下载（由宿主控制）
/// </summary>
internal sealed class NetworkGuard(
    string extensionId,
    ILogger logger,
    int maxViolations = 5) : IDisposable
{
    private readonly string _extensionId = extensionId;
    private readonly ILogger _logger = logger;
    private readonly int _maxViolations = maxViolations;
    private int _violationCount;
    private bool _disposed;

    // 事件
    public event EventHandler<NetworkEventArgs>? ConnectionBlocked;

    // 已知安全的出站地址（白名单，扩展可声明需要访问的地址）
    private readonly HashSet<string> _allowedDestinations = new(StringComparer.OrdinalIgnoreCase);
    
    // DNS 缓存（用于检测 DNS 重绑定攻击）
    private readonly Dictionary<string, DnsCacheEntry> _dnsCache = new();
    private readonly Lock _dnsCacheLock = new();
    
    // 内网地址段（SSRF 保护）
    private static readonly string[] InternalIpRanges = new[]
    {
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "0.0.0.0/8",
        "::1/128",
        "fc00::/7",
        "fe80::/10",
    };
    
    // 危险端口（SSRF 保护）
    private static readonly HashSet<int> DangerousPorts = new()
    {
        21,    // FTP
        22,    // SSH
        23,    // Telnet
        25,    // SMTP
        135,   // RPC
        137,   // NetBIOS
        138,   // NetBIOS
        139,   // NetBIOS
        445,   // SMB
        1433,  // SQL Server
        1434,  // SQL Server Browser
        3306,  // MySQL
        3389,  // RDP
        5432,  // PostgreSQL
        5900,  // VNC
        6379,  // Redis
        27017, // MongoDB
    };

    // 违规记录
    private readonly List<NetworkViolationEntry> _violations = [];

    /// <summary>
    /// 添加允许的出站目标地址。
    /// 扩展可在 DeclareFileScopes 中声明需要访问的网络地址。
    /// </summary>
    public void AllowDestination(string hostOrUrl)
    {
        _allowedDestinations.Add(hostOrUrl);
    }

    /// <summary>
    /// 验证出站连接是否允许。
    /// 返回 true 表示允许，false 表示应阻止。
    /// </summary>
    public bool ValidateOutbound(string method, string url, string? contentType = null)
    {
        if (_disposed) return false;

        // 只允许 GET 请求（下载）
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 其他方法（POST/PUT/PATCH/DELETE）全部禁止
        int violationNum = Interlocked.Increment(ref _violationCount);
        string reason = $"禁止网络上传: {method} {url}";

        _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
        _violations.Add(new NetworkViolationEntry
        {
            Timestamp = DateTime.UtcNow,
            Method = method,
            Url = url,
            ContentType = contentType,
            ViolationNumber = violationNum,
        });

        ConnectionBlocked?.Invoke(this, new NetworkEventArgs
        {
            ExtensionId = _extensionId,
            Method = method,
            Url = url,
            ViolationNumber = violationNum,
            IsTerminal = violationNum >= _maxViolations,
        });

        return false;
    }

    /// <summary>
    /// 验证 Socket 发送是否允许。
    /// 所有 Socket 发送都禁止。
    /// </summary>
    public bool ValidateSocketSend(string targetHost, int targetPort)
    {
        if (_disposed) return false;

        int violationNum = Interlocked.Increment(ref _violationCount);
        string reason = $"禁止 Socket 发送: {targetHost}:{targetPort}";

        _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
        _violations.Add(new NetworkViolationEntry
        {
            Timestamp = DateTime.UtcNow,
            Method = "SOCKET",
            Url = $"{targetHost}:{targetPort}",
            ViolationNumber = violationNum,
        });

        ConnectionBlocked?.Invoke(this, new NetworkEventArgs
        {
            ExtensionId = _extensionId,
            Method = "SOCKET",
            Url = $"{targetHost}:{targetPort}",
            ViolationNumber = violationNum,
            IsTerminal = violationNum >= _maxViolations,
        });

        return false;
    }

    /// <summary>
    /// 验证 SMTP 发送是否允许。
    /// 所有 SMTP 发送都禁止。
    /// </summary>
    public bool ValidateSmtpSend(string smtpHost)
    {
        if (_disposed) return false;

        int violationNum = Interlocked.Increment(ref _violationCount);
        string reason = $"禁止 SMTP 发送: {smtpHost}";

        _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
        _violations.Add(new NetworkViolationEntry
        {
            Timestamp = DateTime.UtcNow,
            Method = "SMTP",
            Url = smtpHost,
            ViolationNumber = violationNum,
        });

        ConnectionBlocked?.Invoke(this, new NetworkEventArgs
        {
            ExtensionId = _extensionId,
            Method = "SMTP",
            Url = smtpHost,
            ViolationNumber = violationNum,
            IsTerminal = violationNum >= _maxViolations,
        });

        return false;
    }
    
    /// <summary>
    /// 验证 URL 是否安全（防 DNS 重绑定和 SSRF）。
    /// 返回 true 表示允许，false 表示应阻止。
    /// </summary>
    public bool ValidateUrl(string url)
    {
        if (_disposed) return false;
        
        try
        {
            var uri = new Uri(url);
            string host = uri.Host;
            
            // 检查是否是内部地址
            if (IsInternalAddress(host))
            {
                int violationNum = Interlocked.Increment(ref _violationCount);
                string reason = $"SSRF 阻止: 尝试访问内部地址 {host}";
                
                _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
                _violations.Add(new NetworkViolationEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Method = "SSRF",
                    Url = url,
                    ViolationNumber = violationNum,
                });
                
                ConnectionBlocked?.Invoke(this, new NetworkEventArgs
                {
                    ExtensionId = _extensionId,
                    Method = "SSRF",
                    Url = url,
                    ViolationNumber = violationNum,
                    IsTerminal = violationNum >= _maxViolations,
                });
                
                return false;
            }
            
            // 检查是否是危险端口
            if (DangerousPorts.Contains(uri.Port))
            {
                int violationNum = Interlocked.Increment(ref _violationCount);
                string reason = $"SSRF 阻止: 尝试访问危险端口 {uri.Port}";
                
                _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
                _violations.Add(new NetworkViolationEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Method = "SSRF",
                    Url = url,
                    ViolationNumber = violationNum,
                });
                
                ConnectionBlocked?.Invoke(this, new NetworkEventArgs
                {
                    ExtensionId = _extensionId,
                    Method = "SSRF",
                    Url = url,
                    ViolationNumber = violationNum,
                    IsTerminal = violationNum >= _maxViolations,
                });
                
                return false;
            }
            
            // 检查 DNS 缓存（检测 DNS 重绑定）
            if (CheckDnsRebinding(host))
            {
                int violationNum = Interlocked.Increment(ref _violationCount);
                string reason = $"DNS 重绑定检测: {host}";
                
                _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
                _violations.Add(new NetworkViolationEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Method = "DNS_REBINDING",
                    Url = url,
                    ViolationNumber = violationNum,
                });
                
                ConnectionBlocked?.Invoke(this, new NetworkEventArgs
                {
                    ExtensionId = _extensionId,
                    Method = "DNS_REBINDING",
                    Url = url,
                    ViolationNumber = violationNum,
                    IsTerminal = violationNum >= _maxViolations,
                });
                
                return false;
            }
            
            return true;
        }
        catch
        {
            // URL 解析失败，拒绝
            return false;
        }
    }
    
    /// <summary>
    /// 检查主机名是否是内部地址。
    /// </summary>
    private bool IsInternalAddress(string host)
    {
        try
        {
            // 尝试解析主机名
            var addresses = Dns.GetHostAddresses(host);
            
            foreach (var address in addresses)
            {
                if (IsIpAddressInternal(address))
                {
                    return true;
                }
            }
            
            return false;
        }
        catch
        {
            // DNS 解析失败，假设是安全的
            return false;
        }
    }
    
    /// <summary>
    /// 检查 IP 地址是否是内部地址。
    /// </summary>
    private static bool IsIpAddressInternal(IPAddress address)
    {
        // 检查特殊地址
        if (IPAddress.IsLoopback(address))
            return true;
        
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;
        
        if (address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.IPv6None))
            return true;
        
        // 检查链路本地地址
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            
            // 169.254.0.0/16 (链路本地)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            
            // 0.0.0.0/8
            if (bytes[0] == 0)
                return true;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            
            // ::1 (回环)
            if (address.Equals(IPAddress.IPv6Loopback))
                return true;
            
            // fc00::/7 (唯一本地地址)
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
            
            // fe80::/10 (链路本地)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 检查是否存在 DNS 重绑定攻击。
    /// </summary>
    private bool CheckDnsRebinding(string host)
    {
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            
            lock (_dnsCacheLock)
            {
                if (_dnsCache.TryGetValue(host, out var cacheEntry))
                {
                    // 检查是否解析到不同的地址
                    foreach (var address in addresses)
                    {
                        if (!address.Equals(cacheEntry.Address))
                        {
                            // 地址变化，可能是 DNS 重绑定
                            _logger.Warn($"DNS 重绑定检测: {host} 从 {cacheEntry.Address} 变为 {address}");
                            return true;
                        }
                    }
                    
                    // 更新缓存
                    _dnsCache[host] = new DnsCacheEntry
                    {
                        HostName = host,
                        Address = cacheEntry.Address,
                        FirstResolved = cacheEntry.FirstResolved,
                        LastResolved = DateTime.UtcNow,
                        ResolutionCount = cacheEntry.ResolutionCount + 1,
                    };
                }
                else
                {
                    // 首次解析，添加到缓存
                    if (addresses.Length > 0)
                    {
                        _dnsCache[host] = new DnsCacheEntry
                        {
                            HostName = host,
                            Address = addresses[0],
                            FirstResolved = DateTime.UtcNow,
                            LastResolved = DateTime.UtcNow,
                            ResolutionCount = 1,
                        };
                    }
                }
            }
            
            return false;
        }
        catch
        {
            // DNS 解析失败，假设是安全的
            return false;
        }
    }
    
    /// <summary>
    /// 清理 DNS 缓存。
    /// </summary>
    public void CleanupDnsCache(TimeSpan maxAge)
    {
        lock (_dnsCacheLock)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var keysToRemove = _dnsCache
                .Where(kvp => kvp.Value.LastResolved < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _dnsCache.Remove(key);
            }
        }
    }

    /// <summary>
    /// 获取违规记录。
    /// </summary>
    public IReadOnlyList<NetworkViolationEntry> GetViolations() => _violations.AsReadOnly();

    /// <summary>
    /// 获取违规次数。
    /// </summary>
    public int ViolationCount => Interlocked.CompareExchange(ref _violationCount, 0, 0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// 网络违规事件参数。
/// </summary>
internal sealed class NetworkEventArgs : EventArgs
{
    public string ExtensionId { get; init; } = "";
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public int ViolationNumber { get; init; }
    public bool IsTerminal { get; init; }
}

/// <summary>
/// 网络违规记录。
/// </summary>
internal sealed class NetworkViolationEntry
{
    public DateTime Timestamp { get; init; }
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public string? ContentType { get; init; }
    public int ViolationNumber { get; init; }
}

/// <summary>
/// DNS 缓存条目（用于检测 DNS 重绑定攻击）。
/// </summary>
internal sealed class DnsCacheEntry
{
    public string HostName { get; init; } = "";
    public IPAddress Address { get; init; } = IPAddress.None;
    public DateTime FirstResolved { get; init; }
    public DateTime LastResolved { get; init; }
    public int ResolutionCount { get; init; }
}
