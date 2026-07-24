// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Runtime.InteropServices;

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

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true,StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

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
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
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
                    if (!LookupPrivilegeValue(null, lpPrivilegeName, out tokenPrivilege.Privileges.Luid))
                    {
                        bRet = false;
                    }
                    else
                    {
                        tokenPrivilege.PrivilegeCount = 1;
                        tokenPrivilege.Privileges.Attributes = SE_PRIVILEGE_ENABLED;

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

                if (!LookupPrivilegeValue(null, SE_DEBUG_NAME, out tkp.Privileges.Luid))
                {
                    return false;
                }

                tkp.PrivilegeCount = 1;
                tkp.Privileges.Attributes = SE_PRIVILEGE_ENABLED;

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

        private static uint[] _processIdBuffer = new uint[2048];

        private static int GetEmptyAllSet()
        {
            uint[] processIds = _processIdBuffer;
            int cleanedCount = 0;

            while (true)
            {
                if (!EnumProcesses(processIds, (uint)(processIds.Length * sizeof(uint)), out uint bytesReturned))
                {
                    return cleanedCount;
                }

                int numProcesses = (int)(bytesReturned / sizeof(uint));

                if (numProcesses == processIds.Length)
                {
                    processIds = new uint[processIds.Length * 2];
                    _processIdBuffer = processIds;
                    continue;
                }

                for (int i = 0; i < numProcesses; i++)
                {
                    if (processIds[i] == 0) continue;

                    IntPtr hProcess = OpenProcess(ProcessAccessFlags.All, false, processIds[i]);

                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            SetProcessWorkingSetSize(hProcess, -1, -1);
                            EmptyWorkingSet(hProcess);
                            cleanedCount++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MemoryCleaner] Failed to clean working set for process {processIds[i]}: {ex.Message}");
                        }
                        finally
                        {
                            CloseHandle(hProcess);
                        }
                    }
                }

                break;
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


}