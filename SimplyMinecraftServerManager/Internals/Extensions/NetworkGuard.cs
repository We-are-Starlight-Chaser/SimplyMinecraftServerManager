// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using DnsClient;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
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
///   - 禁止 Base64 编码的 URL
///   - 禁止 URL 查询字符串（? 传参）
///   - 120 并发限频，超过阈值直接拒绝
///   - 仅允许通过 IDownloadService 进行 HTTP GET 下载（由宿主控制）
/// </summary>
internal sealed partial class NetworkGuard(
    string extensionId,
    ILogger logger,
    int maxViolations = 5,
    int maxConcurrentRequests = 120) : IDisposable
{
    private readonly string _extensionId = extensionId;
    private readonly ILogger _logger = logger;
    private readonly int _maxViolations = maxViolations;
    private readonly int _maxConcurrentRequests = maxConcurrentRequests;
    private int _violationCount;
    private int _concurrentRequests;
    private bool _disposed;

    // 最大违规记录数（防止内存无限增长）
    private const int MaxViolationEntries = 500;

    // 事件
    public event EventHandler<NetworkEventArgs>? ConnectionBlocked;

    // 已知安全的出站地址（白名单，扩展可声明需要访问的地址）
    private readonly HashSet<string> _allowedDestinations = new(StringComparer.OrdinalIgnoreCase);
    
    // LookupClient（内置 DNS 缓存，替代手动缓存）
    private static readonly LookupClient _lookupClient = new(new LookupClientOptions
    {
        UseCache = true,
        Timeout = TimeSpan.FromSeconds(2),
    });
    
    // DNS 重绑定检测缓存（LookupClient 负责缓存，这里只跟踪历史解析结果）
    private readonly Dictionary<string, DnsCacheEntry> _dnsRebindCache = [];
    private readonly Lock _dnsRebindCacheLock = new();
    
    // 内网地址段（SSRF 保护）
    private static readonly string[] InternalIpRanges =
    [
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "0.0.0.0/8",
        "::1/128",
        "fc00::/7",
        "fe80::/10",
    ];
    
    // 危险端口（SSRF 保护）
    private static readonly HashSet<int> DangerousPorts =
    [
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
    ];

    // 违规记录（线程安全）
    private readonly ConcurrentBag<NetworkViolationEntry> _violations = [];

    /// <summary>
    /// 当前并发请求数。
    /// </summary>
    public int ConcurrentRequests => Interlocked.CompareExchange(ref _concurrentRequests, 0, 0);

    /// <summary>
    /// 添加允许的出站目标地址。
    /// 扩展可在 DeclareFileScopes 中声明需要访问的网络地址。
    /// </summary>
    public void AllowDestination(string hostOrUrl)
    {
        _allowedDestinations.Add(hostOrUrl);
    }

    /// <summary>
    /// 增加并发请求计数。
    /// 在发起请求前调用，返回 false 表示超过并发限制。
    /// </summary>
    public bool TryAcquireRequestSlot()
    {
        if (_disposed) return false;

        int current = Interlocked.Increment(ref _concurrentRequests);
        if (current > _maxConcurrentRequests)
        {
            Interlocked.Decrement(ref _concurrentRequests);
            
            int violationNum = Interlocked.Increment(ref _violationCount);
            _logger.Warn($"[{_extensionId}] 并发限频: 当前 {current} 超过阈值 {_maxConcurrentRequests}");
            if (_violations.Count < MaxViolationEntries)
                AddViolation(new NetworkViolationEntry
            {
                Timestamp = DateTime.UtcNow,
                Method = "RATE_LIMIT",
                Url = $"concurrent={current}",
                ViolationNumber = violationNum,
            });

            ConnectionBlocked?.Invoke(this, new NetworkEventArgs
            {
                ExtensionId = _extensionId,
                Method = "RATE_LIMIT",
                Url = $"concurrent={current}",
                ViolationNumber = violationNum,
                IsTerminal = violationNum >= _maxViolations,
            });

            return false;
        }
        return true;
    }

    /// <summary>
    /// 释放并发请求计数。
    /// 在请求完成后调用。
    /// </summary>
    public void ReleaseRequestSlot()
    {
        Interlocked.Decrement(ref _concurrentRequests);
    }

    /// <summary>
    /// 验证出站连接是否允许。
    /// 返回 true 表示允许，false 表示应阻止。
    /// </summary>
    public bool ValidateOutbound(string method, string url, string? contentType = null)
    {
        if (_disposed) return false;

        // 并发限频检查
        if (!TryAcquireRequestSlot())
        {
            return false;
        }

        // 只允许 GET 请求（下载）
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            // 验证 URL 安全性
            return ValidateUrlFormat(url);
        }

        // 其他方法（POST/PUT/PATCH/DELETE）全部禁止
        ReleaseRequestSlot();
        int violationNum = Interlocked.Increment(ref _violationCount);

        _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: 禁止网络上传: {method} {url}");
        AddViolation(new NetworkViolationEntry
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
    /// 验证 URL 格式是否安全（防 Base64 编码、查询字符串）。
    /// </summary>
    private bool ValidateUrlFormat(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ReleaseRequestSlot();
            return false;
        }

        // 检查 URL 是否包含 Base64 编码内容
        if (ContainsBase64Pattern(url))
        {
            ReleaseRequestSlot();
            int violationNum = Interlocked.Increment(ref _violationCount);
            string reason = "禁止 Base64 编码的 URL";

            _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
            AddViolation(new NetworkViolationEntry
            {
                Timestamp = DateTime.UtcNow,
                Method = "BASE64",
                Url = url,
                ViolationNumber = violationNum,
            });

            ConnectionBlocked?.Invoke(this, new NetworkEventArgs
            {
                ExtensionId = _extensionId,
                Method = "BASE64",
                Url = url,
                ViolationNumber = violationNum,
                IsTerminal = violationNum >= _maxViolations,
            });

            return false;
        }

        // 检查 URL 是否包含查询字符串（? 传参）
        if (url.Contains('?'))
        {
            ReleaseRequestSlot();
            int violationNum = Interlocked.Increment(ref _violationCount);
            string reason = "禁止 URL 查询字符串传参";

            _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
            AddViolation(new NetworkViolationEntry
            {
                Timestamp = DateTime.UtcNow,
                Method = "QUERY_STRING",
                Url = url,
                ViolationNumber = violationNum,
            });

            ConnectionBlocked?.Invoke(this, new NetworkEventArgs
            {
                ExtensionId = _extensionId,
                Method = "QUERY_STRING",
                Url = url,
                ViolationNumber = violationNum,
                IsTerminal = violationNum >= _maxViolations,
            });

            return false;
        }

        return true;
    }

    /// <summary>
    /// 检测字符串是否包含 Base64 编码模式。
    /// </summary>
    private static bool ContainsBase64Pattern(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length < 20)
            return false;

        // 使用 IsMatch 避免 MatchCollection 分配
        if (!Base64Pattern().IsMatch(input))
            return false;

        // 找到匹配后验证是否真的是有效的 Base64
        var match = Base64Pattern().Match(input);
        return match.Success && IsValidBase64(match.Value);
    }

    /// <summary>
    /// 验证字符串是否是有效的 Base64 编码。
    /// </summary>
    private static bool IsValidBase64(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length % 4 != 0)
            return false;

        try
        {
            _ = Convert.FromBase64String(input);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
    
    /// <summary>
    /// 验证 URL 是否安全（防 DNS 重绑定和 SSRF）。
    /// 返回 true 表示允许，false 表示应阻止。
    /// </summary>
    public async Task<bool> ValidateUrl(string url)
    {
        if (_disposed) return false;
        
        try
        {
            var uri = new Uri(url);
            string host = uri.Host;
            
            // 检查是否是内部地址
            if (await IsInternalAddress(host))
            {
                int violationNum = Interlocked.Increment(ref _violationCount);
                string reason = $"SSRF 阻止: 尝试访问内部地址 {host}";
                
                _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
                AddViolation(new NetworkViolationEntry
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
                AddViolation(new NetworkViolationEntry
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
            if (await CheckDnsRebinding(host))
            {
                int violationNum = Interlocked.Increment(ref _violationCount);
                string reason = $"DNS 重绑定检测: {host}";
                
                _logger.Warn($"[{_extensionId}] 网络违规 #{violationNum}: {reason}");
                AddViolation(new NetworkViolationEntry
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
    /// 检查主机名是否是内部地址（LookupClient 内置缓存）。
    /// </summary>
    private static async Task<bool> IsInternalAddress(string host)
    {
        try
        {
            var result = await _lookupClient.QueryAsync(host, QueryType.A);
            var aRecords = result.Answers.ARecords();
            foreach (var record in aRecords)
            {
                if (IsIpAddressInternal(record.Address))
                    return true;
            }

            var resultV6 = await _lookupClient.QueryAsync(host, QueryType.AAAA);
            var aaaaRecords = resultV6.Answers.AaaaRecords();
            foreach (var record in aaaaRecords)
            {
                if (IsIpAddressInternal(record.Address))
                    return true;
            }

            return false;
        }
        catch
        {
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
    private async Task<bool> CheckDnsRebinding(string host)
    {
        try
        {
            var result = await _lookupClient.QueryAsync(host, QueryType.A);
            var addresses = result.Answers.ARecords().Select(r => r.Address).ToArray();
            if (addresses.Length == 0)
            {
                var resultV6 = await _lookupClient.QueryAsync(host, QueryType.AAAA);
                addresses = [.. resultV6.Answers.AaaaRecords().Select(r => r.Address)];
            }
            
            lock (_dnsRebindCacheLock)
            {
                if (_dnsRebindCache.TryGetValue(host, out var cacheEntry))
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
                    _dnsRebindCache[host] = new DnsCacheEntry
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
                        _dnsRebindCache[host] = new DnsCacheEntry
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
        lock (_dnsRebindCacheLock)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var keysToRemove = _dnsRebindCache
                .Where(kvp => kvp.Value.LastResolved < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _dnsRebindCache.Remove(key);
            }
        }
    }

    /// <summary>
    /// 添加违规记录（带大小限制，防止内存无限增长）。
    /// </summary>
    private void AddViolation(NetworkViolationEntry entry)
    {
        if (_violations.Count < MaxViolationEntries)
            _violations.Add(entry);
    }

    /// <summary>
    /// 获取违规记录。
    /// </summary>
    public IReadOnlyList<NetworkViolationEntry> GetViolations() => [.. _violations];

    /// <summary>
    /// 获取违规次数。
    /// </summary>
    public int ViolationCount => Interlocked.CompareExchange(ref _violationCount, 0, 0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    [GeneratedRegex(@"[A-Za-z0-9+/]{20,}={0,2}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex Base64Pattern();
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
