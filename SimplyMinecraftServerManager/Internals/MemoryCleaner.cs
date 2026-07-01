// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 内存清理器类，提供清理系统内存工作集的功能
    /// </summary>
    public partial class MemoryCleaner
    {
        #region Windows API Imports

        [LibraryImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EmptyWorkingSet(IntPtr hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetProcessWorkingSetSize(IntPtr hProcess, int minimumWorkingSetSize, int maximumWorkingSetSize);

        [LibraryImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumProcesses([Out] uint[] processIds, uint cb, out uint pBytesReturned);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr OpenProcess(ProcessAccessFlags processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetSystemFileCacheSize(int MinimumFileCacheSize, int MaximumFileCacheSize, uint Flags);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenProcessToken(IntPtr ProcessHandle, TokenAccessLevels DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass,
            IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

        #endregion

        #region Enums and Structures

        [Flags]
        public enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000
        }

        [Flags]
        public enum TokenAccessLevels
        {
            AssignPrimary = 0x00000001,
            Duplicate = 0x00000002,
            Impersonate = 0x00000004,
            Query = 0x00000008,
            QuerySource = 0x00000010,
            AdjustPrivileges = 0x00000020,
            AdjustGroups = 0x00000040,
            AdjustDefault = 0x00000080,
            AdjustSessionId = 0x00000100,
            Read = 0x00020008,
            Write = 0x000200E0,
            Execute = 0x00020000,
            AllAccess = 0x000F01FF
        }

        public enum TOKEN_INFORMATION_CLASS
        {
            TokenUser = 1,
            TokenGroups,
            TokenPrivileges,
            TokenOwner,
            TokenPrimaryGroup,
            TokenDefaultDacl,
            TokenSource,
            TokenType,
            TokenImpersonationLevel,
            TokenStatistics,
            TokenRestrictedSids,
            TokenSessionId,
            TokenGroupsAndPrivileges,
            TokenSessionReference,
            TokenSandBoxInert,
            TokenAuditPolicy,
            TokenOrigin,
            TokenElevationType,
            TokenLinkedToken,
            TokenElevation,
            TokenHasRestrictions,
            TokenAccessInformation,
            TokenVirtualizationAllowed,
            TokenVirtualizationEnabled,
            TokenIntegrityLevel,
            TokenUIAccess,
            TokenMandatoryPolicy,
            TokenLogonSid,
            MaxTokenInfoClass
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_ELEVATION
        {
            public uint TokenIsElevated;
        }

        #endregion

        #region Constants

        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_DEBUG_NAME = "SeDebugPrivilege";
        private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

        #endregion

        #region Properties

        /// <summary>
        /// 是否正在运行内存清理
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 是否具有管理员权限
        /// </summary>
        public static bool IsElevated => IsProcessElevated;

        #endregion

        #region Public Methods

        /// <summary>
        /// 立即执行一次内存清理
        /// </summary>
        /// <returns>清理的进程数量</returns>
        public int CleanMemory()
        {
            if (!IsProcessElevated)
            {
                throw new InvalidOperationException("此操作需要管理员权限，请以管理员身份运行应用程序");
            }

            // 提升权限
            bool debugPrivilegeResult = AdjustTokenPrivilegesForNT();
            bool quotaPrivilegeResult = EnableSpecificPrivilege(SE_INCREASE_QUOTA_NAME);

            int cleanedProcesses = GetEmptyAllSet();


            bool cacheCleanupResult = SetSystemFileCacheSize(-1, -1, 0);


            return cleanedProcesses;
        }

        /// <summary>
        /// 开始定期清理内存
        /// </summary>
        /// <param name="intervalSeconds">清理间隔（秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async System.Threading.Tasks.Task StartPeriodicCleanAsync(int intervalSeconds,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (!IsProcessElevated)
            {
                throw new InvalidOperationException("此操作需要管理员权限，请以管理员身份运行应用程序");
            }

            IsRunning = true;

            try
            {
                // 提升权限
                bool debugPrivilegeResult = AdjustTokenPrivilegesForNT();
                bool quotaPrivilegeResult = EnableSpecificPrivilege(SE_INCREASE_QUOTA_NAME);

                while (!cancellationToken.IsCancellationRequested && IsRunning)
                {

                    await Task.Delay(intervalSeconds * 1000, cancellationToken);

                    if (cancellationToken.IsCancellationRequested || !IsRunning)
                        break;

                    int cleanedProcesses = GetEmptyAllSet();

                    bool cacheCleanupResult = SetSystemFileCacheSize(-1, -1, 0);
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// 停止定期清理
        /// </summary>
        public void StopPeriodicClean()
        {
            IsRunning = false;
        }

        #endregion

        #region Private Helper Methods

        private static bool EnableSpecificPrivilege(string lpPrivilegeName)
        {
            IntPtr hToken = IntPtr.Zero;
            TOKEN_PRIVILEGES tokenPrivilege = new();
            bool bRet = true;

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(),
                    TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges,
                    out hToken))
                {
                    bRet = false;
                }
                else
                {
                    if (!LookupPrivilegeValue(null, lpPrivilegeName, out tokenPrivilege.Luid))
                    {
                        bRet = false;
                    }
                    else
                    {
                        tokenPrivilege.PrivilegeCount = 1;
                        tokenPrivilege.Attributes = SE_PRIVILEGE_ENABLED;

                        if (!AdjustTokenPrivileges(hToken, false, ref tokenPrivilege, 0, IntPtr.Zero, IntPtr.Zero))
                        {
                            bRet = false;
                        }
                    }
                }
            }
            finally
            {
                if (hToken != IntPtr.Zero)
                {
                    CloseHandle(hToken);
                }
            }

            return bRet;
        }

        private static bool AdjustTokenPrivilegesForNT()
        {
            IntPtr hToken = IntPtr.Zero;
            TOKEN_PRIVILEGES tkp = new();

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(),
                    TokenAccessLevels.AdjustPrivileges | TokenAccessLevels.Query,
                    out hToken))
                {
                    return false;
                }

                if (!LookupPrivilegeValue(null, SE_DEBUG_NAME, out tkp.Luid))
                {
                    return false;
                }

                tkp.PrivilegeCount = 1;
                tkp.Attributes = SE_PRIVILEGE_ENABLED;

                return AdjustTokenPrivileges(hToken, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                if (hToken != IntPtr.Zero)
                {
                    CloseHandle(hToken);
                }
            }
        }

        private static int GetEmptyAllSet()
        {
            uint[] processIds = new uint[1024];

            if (!EnumProcesses(processIds, (uint)(processIds.Length * sizeof(uint)), out uint bytesReturned))
            {
                return 0;
            }

            int numProcesses = (int)(bytesReturned / sizeof(uint));
            int cleanedCount = 0;

            for (int i = 0; i < numProcesses; i++)
            {
                if (processIds[i] == 0) continue; // Skip null process IDs

                IntPtr hProcess = OpenProcess(ProcessAccessFlags.All, false, processIds[i]);

                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        SetProcessWorkingSetSize(hProcess, -1, -1);
                        EmptyWorkingSet(hProcess);
                        cleanedCount++;
                    }
                    catch (Exception)
                    {
                        // 忽略单个进程清理时的错误
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
            }

            return cleanedCount;
        }

        private static bool IsProcessElevated
        {
            get
            {
                IntPtr hToken = IntPtr.Zero;
                bool fIsElevated = false;

                try
                {
                    if (OpenProcessToken(GetCurrentProcess(), TokenAccessLevels.Query, out hToken))
                    {
                        IntPtr elevationPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TOKEN_ELEVATION>());

                        if (GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation,
                            elevationPtr, (uint)Marshal.SizeOf<TOKEN_ELEVATION>(), out uint returnLength))
                        {
                            TOKEN_ELEVATION elevation = Marshal.PtrToStructure<TOKEN_ELEVATION>(elevationPtr);
                            fIsElevated = elevation.TokenIsElevated != 0;
                        }

                        Marshal.FreeHGlobal(elevationPtr);
                    }
                }
                finally
                {
                    if (hToken != IntPtr.Zero)
                    {
                        CloseHandle(hToken);
                    }
                }

                return fIsElevated;
            }
        }

        #endregion
    }

    /// <summary>
    /// 内存清理进度事件参数
    /// </summary>
    public class MemoryCleanProgressEventArgs(string message) : EventArgs
    {
        /// <summary>
        /// 进度消息
        /// </summary>
        public string Message { get; } = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// 内存清理完成事件参数
    /// </summary>
    public class MemoryCleanCompleteEventArgs(int processCount, bool debugPrivilegeResult,
        bool quotaPrivilegeResult, bool cacheCleanupResult) : EventArgs
    {
        /// <summary>
        /// 清理的进程数量
        /// </summary>
        public int ProcessCount { get; } = processCount;

        /// <summary>
        /// 调试权限设置结果
        /// </summary>
        public bool DebugPrivilegeResult { get; } = debugPrivilegeResult;

        /// <summary>
        /// 配额权限设置结果
        /// </summary>
        public bool QuotaPrivilegeResult { get; } = quotaPrivilegeResult;

        /// <summary>
        /// 缓存清理结果
        /// </summary>
        public bool CacheCleanupResult { get; } = cacheCleanupResult;
    }
}