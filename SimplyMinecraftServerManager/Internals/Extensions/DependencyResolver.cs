// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展依赖解析器。
/// 负责分层拓扑排序、版本兼容性检查、循环依赖检测。
/// 支持最高 16 层分级，同层扩展可并行加载。
/// </summary>
internal sealed class DependencyResolver(Dictionary<string, IExtensionMetadata> extensions, ILogger logger)
{
    private readonly Dictionary<string, IExtensionMetadata> _extensions = extensions;
    private readonly ILogger _logger = logger;

    /// <summary>最大分层数</summary>
    public const int MaxTiers = 16;

    /// <summary>
    /// 分层拓扑排序。
    /// 将扩展按依赖深度分为多个层（tier），每层内的扩展无相互依赖，可并行加载。
    /// Tier 0 = 无依赖的扩展，Tier N = 所有依赖均在 Tier 0..N-1 中。
    /// </summary>
    /// <returns>按层排列的扩展 ID 列表（List&lt;List&lt;string&gt;&gt;，外层=层序，内层=同层扩展）</returns>
    public List<List<string>> ResolveTiers()
    {
        // 1. 构建依赖图和入度表
        var inDegree = new Dictionary<string, int>();
        var dependents = new Dictionary<string, List<string>>();
        var dependencies = new Dictionary<string, List<string>>();

        foreach (var (id, _) in _extensions)
        {
            inDegree.TryAdd(id, 0);
            dependents.TryAdd(id, []);
            dependencies.TryAdd(id, []);
        }

        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;

            foreach (var dep in meta.Dependencies)
            {
                if (!_extensions.ContainsKey(dep.Id))
                {
                    throw new InvalidOperationException(
                        $"扩展 '{id}' 依赖 '{dep.Id}'，但该依赖未找到。");
                }

                dependents[dep.Id].Add(id);
                dependencies[id].Add(dep.Id);
                inDegree[id] = inDegree.GetValueOrDefault(id) + 1;
            }
        }

        // 2. 分层 Kahn 算法：记录每个扩展所在层级
        var tierMap = new Dictionary<string, int>();
        var queue = new Queue<string>();

        // Tier 0: 无依赖的扩展
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
            {
                tierMap[id] = 0;
                queue.Enqueue(id);
            }
        }

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            int currentTier = tierMap[current];

            foreach (string dependent in dependents[current])
            {
                inDegree[dependent]--;

                // 依赖者的层级 = max(所有已处理依赖者的层级) + 1
                // 通过维护依赖者已知的最大层级来实现
                int dependentTier = currentTier + 1;

                // 确保取所有依赖的最大层级
                if (tierMap.TryGetValue(dependent, out int existingTier))
                {
                    if (dependentTier > existingTier)
                    {
                        tierMap[dependent] = dependentTier;
                    }
                    else
                    {
                        dependentTier = existingTier;
                    }
                }
                else
                {
                    tierMap[dependent] = dependentTier;
                }

                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        // 3. 循环依赖检测
        if (tierMap.Count != _extensions.Count)
        {
            var circular = string.Join(", ", _extensions.Keys.Where(k => !tierMap.ContainsKey(k)));
            throw new InvalidOperationException(
                $"检测到循环依赖，涉及扩展: {circular}");
        }

        // 4. 限制最大层数
        int maxTier = tierMap.Values.DefaultIfEmpty(0).Max();
        if (maxTier >= MaxTiers)
        {
            _logger.Warn($"扩展依赖层级 ({maxTier + 1}) 超过最大限制 ({MaxTiers})，将截断");
        }

        // 5. 按层级分组
        int tierCount = Math.Min(maxTier + 1, MaxTiers);
        var tiers = new List<List<string>>(tierCount);
        for (int i = 0; i < tierCount; i++)
        {
            tiers.Add([]);
        }

        foreach (var (id, tier) in tierMap)
        {
            int clampedTier = Math.Min(tier, MaxTiers - 1);
            tiers[clampedTier].Add(id);
        }

        // 移除空层
        tiers.RemoveAll(t => t.Count == 0);

        return tiers;
    }

    /// <summary>
    /// 拓扑排序扩展加载顺序（Kahn 算法）。
    /// 返回按依赖顺序排列的扩展 ID 列表。
    /// 如果存在循环依赖或缺失依赖，抛出异常。
    /// </summary>
    public List<string> Resolve()
    {
        var inDegree = new Dictionary<string, int>();
        var dependents = new Dictionary<string, List<string>>();

        foreach (var (id, _) in _extensions)
        {
            inDegree.TryAdd(id, 0);
            dependents.TryAdd(id, []);
        }

        // 构建依赖图
        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;

            foreach (var dep in meta.Dependencies)
            {
                if (!_extensions.ContainsKey(dep.Id))
                {
                    throw new InvalidOperationException(
                        $"扩展 '{id}' 依赖 '{dep.Id}'，但该依赖未找到。");
                }

                dependents[dep.Id].Add(id);
                inDegree[id] = inDegree.GetValueOrDefault(id) + 1;
            }
        }

        // Kahn 拓扑排序
        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0) queue.Enqueue(id);
        }

        var sorted = new List<string>();
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            sorted.Add(current);

            foreach (string dependent in dependents[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (sorted.Count != _extensions.Count)
        {
            var circular = string.Join(", ", _extensions.Keys.Except(sorted));
            throw new InvalidOperationException(
                $"检测到循环依赖，涉及扩展: {circular}");
        }

        return sorted;
    }

    /// <summary>
    /// 验证所有扩展的 HostApiVersion 兼容性。
    /// 扩展声明所需最低宿主 SDK 版本，与当前宿主版本比较。
    /// 返回不兼容的扩展 ID 列表。
    /// </summary>
    public List<string> CheckHostCompatibility(Version hostSdkVersion)
    {
        var incompatible = new List<string>();

        foreach (var (id, meta) in _extensions)
        {
            if (meta.HostApiVersion is not null && hostSdkVersion < meta.HostApiVersion)
            {
                _logger.Warn($"扩展 '{id}' 需要 SDK >= {meta.HostApiVersion}，当前主程序 SDK 版本 {hostSdkVersion}");
                incompatible.Add(id);
            }
        }

        return incompatible;
    }

    /// <summary>
    /// 验证所有依赖的版本范围是否满足。
    /// </summary>
    public List<string> ValidateDependencyVersions()
    {
        var invalid = new List<string>();

        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;

            foreach (var dep in meta.Dependencies)
            {
                if (_extensions.TryGetValue(dep.Id, out var depMeta) && dep.Range is not null)
                {
                    if (!dep.Range.Satisfies(depMeta.Version))
                    {
                        _logger.Warn(
                            $"扩展 '{id}' 依赖 '{dep.Id}' 版本 {depMeta.Version}，" +
                            $"但要求版本范围 {dep.Range}");
                        invalid.Add(id);
                    }
                }
            }
        }

        return invalid;
    }
}
