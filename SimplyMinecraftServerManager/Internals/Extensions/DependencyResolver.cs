// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展依赖解析器（启动期单次调用专用）。
/// Resolve 与 ResolveTiers 互斥调用，均原地破坏入度表，零冗余拷贝。
/// </summary>
internal sealed class DependencyResolver(
    Dictionary<string, IExtensionMetadata> extensions,
    ILogger logger)
{
    private readonly Dictionary<string, IExtensionMetadata> _extensions = extensions;
    private readonly ILogger _logger = logger;

    public const int MaxTiers = 16;

    // ========== 共享图结构（仅作为参数传递，不持有状态） ==========
    private readonly struct DepGraph(
        Dictionary<string, int> inDegree,
        Dictionary<string, List<string>> dependents)
    {
        public readonly Dictionary<string, int> InDegree = inDegree;
        public readonly Dictionary<string, List<string>> Dependents = dependents;
    }

    /// <summary>
    /// 构建依赖图 + 前置校验（纯函数，无副作用）。
    /// 由 Resolve / ResolveTiers 在入口处调用一次。
    /// </summary>
    private DepGraph BuildGraph()
    {
        int count = _extensions.Count;
        var inDegree = new Dictionary<string, int>(count);
        var dependents = new Dictionary<string, List<string>>(count);

        foreach (var id in _extensions.Keys)
        {
            inDegree[id] = 0;
            dependents[id] = [];
        }

        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;

            for (int i = 0; i < meta.Dependencies.Length; i++)
            {
                var dep = meta.Dependencies[i];

                if (dep.Id == id)
                    throw new InvalidOperationException($"扩展 '{id}' 存在自依赖。");

                if (!_extensions.ContainsKey(dep.Id))
                    throw new InvalidOperationException(
                        $"扩展 '{id}' 依赖 '{dep.Id}'，但该依赖未找到。");

                // 线性去重（依赖数通常 < 10，避免 HashSet 分配）
                bool dup = false;
                for (int j = 0; j < i; j++)
                    if (meta.Dependencies[j].Id == dep.Id) { dup = true; break; }
                if (dup) continue;

                dependents[dep.Id].Add(id);
                inDegree[id]++;
            }
        }

        return new DepGraph(inDegree, dependents);
    }

    /// <summary>
    /// 串行拓扑排序（层级 &lt; MaxTiers 时调用）。
    /// 原地破坏 inDegree，不可重复调用。
    /// </summary>
    public List<string> Resolve()
    {
        var graph = BuildGraph();
        int count = _extensions.Count;
        if (count == 0) return [];

        // 零分配 BFS 队列
        var queue = new string[count];
        int head = 0, tail = 0;

        foreach (var (id, degree) in graph.InDegree)
            if (degree == 0) queue[tail++] = id;

        var sorted = new List<string>(count);
        while (head < tail)
        {
            var current = queue[head++];
            sorted.Add(current);

            foreach (var dependent in graph.Dependents[current])
            {
                graph.InDegree[dependent]--; // 原地破坏
                if (graph.InDegree[dependent] == 0)
                    queue[tail++] = dependent;
            }
        }

        if (sorted.Count != count)
            throw new InvalidOperationException(
                $"检测到循环依赖，涉及扩展: {string.Join(", ", _extensions.Keys.Except(sorted))}");

        return sorted;
    }

    /// <summary>
    /// 分层拓扑排序（层级 &gt;= MaxTiers 时调用）。
    /// 原地破坏 inDegree，不可重复调用。
    /// </summary>
    public List<List<string>> ResolveTiers()
    {
        var graph = BuildGraph();
        int count = _extensions.Count;
        if (count == 0) return [];

        var tierMap = new Dictionary<string, int>(count);

        // 零分配 BFS 队列
        var queue = new string[count];
        int head = 0, tail = 0;

        foreach (var (id, degree) in graph.InDegree)
        {
            if (degree == 0)
            {
                tierMap[id] = 0;
                queue[tail++] = id;
            }
        }

        while (head < tail)
        {
            var current = queue[head++];
            int currentTier = tierMap[current];

            foreach (var dependent in graph.Dependents[current])
            {
                graph.InDegree[dependent]--; // 原地破坏

                // CMOV 无分支层级更新
                tierMap[dependent] = Math.Max(
                    tierMap.GetValueOrDefault(dependent, -1),
                    currentTier + 1);

                if (graph.InDegree[dependent] == 0)
                    queue[tail++] = dependent;
            }
        }

        if (tierMap.Count != count)
            throw new InvalidOperationException(
                $"检测到循环依赖，涉及扩展: {string.Join(", ", _extensions.Keys.Where(k => !tierMap.ContainsKey(k)))}");

        // 精确预分配分层结果
        int maxTier = 0;
        foreach (var t in tierMap.Values)
            if (t > maxTier) maxTier = t;

        if (maxTier >= MaxTiers)
            _logger.Warn($"扩展依赖层级 ({maxTier + 1}) 超过最大限制 ({MaxTiers})，将截断");

        int tierCount = Math.Min(maxTier + 1, MaxTiers);

        // 第一遍：统计每层数量
        var tierSizes = new int[tierCount];
        foreach (var t in tierMap.Values)
            tierSizes[Math.Min(t, MaxTiers - 1)]++;

        // 第二遍：按精确容量填充
        var buckets = new List<string>?[tierCount];
        for (int i = 0; i < tierCount; i++)
            if (tierSizes[i] > 0) buckets[i] = new List<string>(tierSizes[i]);

        foreach (var (id, t) in tierMap)
            buckets[Math.Min(t, MaxTiers - 1)]!.Add(id);

        var result = new List<List<string>>(tierCount);
        for (int i = 0; i < tierCount; i++)
            if (buckets[i] is not null) result.Add(buckets[i]);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public List<string> CheckHostCompatibility(Version hostSdkVersion)
    {
        var list = new List<string>();
        foreach (var (id, meta) in _extensions)
        {
            if (meta.HostApiVersion is not null && hostSdkVersion < meta.HostApiVersion)
            {
                _logger.Warn($"扩展 '{id}' 需要 SDK >= {meta.HostApiVersion}，当前 {hostSdkVersion}");
                list.Add(id);
            }
        }
        return list;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public List<string> ValidateDependencyVersions()
    {
        var list = new List<string>();
        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;
            foreach (var dep in meta.Dependencies)
            {
                if (_extensions.TryGetValue(dep.Id, out var m) && dep.Range is not null && !dep.Range.Satisfies(m.Version))
                {
                    _logger.Warn($"扩展 '{id}' 依赖 '{dep.Id}' v{m.Version}，要求 {dep.Range}");
                    list.Add(id);
                }
            }
        }
        return list;
    }
}