// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 能力守卫：在扩展调用服务前检查 RequiredCapabilities。
/// 未声明的能力会被拒绝，防止越权访问。
/// </summary>
internal sealed class CapabilityGuard(ExtensionCapability granted)
{
    /// <summary>检查扩展是否拥有所需能力，否则抛出 UnauthorizedAccessException</summary>
    public void Ensure(ExtensionCapability required)
    {
        if (!granted.HasFlag(required))
        {
            throw new UnauthorizedAccessException(
                $"扩展未声明所需能力 '{required}'，无法执行此操作。请在 [Extension] 特性中声明该能力。");
        }
    }

    /// <summary>尝试检查能力，返回是否拥有</summary>
    public bool Has(ExtensionCapability required) => granted.HasFlag(required);
}
