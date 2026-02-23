using System.Runtime.InteropServices;

namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// CPU 架构。
    /// </summary>
    public enum JdkArchitecture
    {
        X64,
        AArch64
    }

    public static class JdkArchitectureHelper
    {
        /// <summary>
        /// 自动检测当前系统架构。
        /// </summary>
        public static JdkArchitecture Current =>
            RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => JdkArchitecture.AArch64,
                _ => JdkArchitecture.X64
            };
    }
}