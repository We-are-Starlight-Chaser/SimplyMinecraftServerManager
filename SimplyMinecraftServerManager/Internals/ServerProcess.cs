using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    public partial class ServerProcess(string instanceId) : IDisposable
    {
        private Process? _process;
        private bool _disposed;
        private IntPtr _jobHandle = IntPtr.Zero;

        public string InstanceId { get; } = instanceId;

        public bool IsRunning => _process is { HasExited: false };

        public event EventHandler<string>? OutputReceived;
        public event EventHandler<string>? ErrorReceived;
        public event EventHandler<int>? Exited;

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr CreateJobObjectA(IntPtr lpJobAttributes, string lpName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(IntPtr hJob, uint JobObjectInfoClass, IntPtr lpInfo, uint cbInfo);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial uint GetLastError();

        private const uint JOB_OBJECT_LIMIT_PROCESS_TIME = 0x00000002;
        private const uint JOB_OBJECT_LIMIT_MEMORY = 0x00000001;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const uint JobObjectExtendedLimitInformation = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessTimeLimit;
            public long PerJobTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public long Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private void CreateJobObject()
        {
            _jobHandle = CreateJobObjectA(IntPtr.Zero, $"SMSM_{InstanceId}_{Guid.NewGuid():N}");
            if (_jobHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create job object: {GetLastError()}");
            }

            var limitInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int size = Marshal.SizeOf(limitInfo);
            IntPtr limitInfoPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limitInfo, limitInfoPtr, false);
                if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, limitInfoPtr, (uint)size))
                {
                    throw new InvalidOperationException($"Failed to set job object limits: {GetLastError()}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(limitInfoPtr);
            }
        }

        public void Start()
        {
            if (IsRunning)
                throw new InvalidOperationException("Server is already running.");

            var info = InstanceManager.GetById(InstanceId)
                ?? throw new InvalidOperationException($"Instance '{InstanceId}' not found.");

            string javaPath = InstanceManager.ResolveJdkPath(InstanceId);
            if (string.IsNullOrWhiteSpace(javaPath))
                throw new InvalidOperationException("JDK path is not configured.");

            if (!SecurityHelper.IsPathTraversal(javaPath))
                throw new InvalidOperationException("Invalid JDK path");

            string workDir = PathHelper.GetInstanceDir(InstanceId);
            string jarPath = PathHelper.GetServerJarPath(InstanceId, info.ServerJar);

            if (!File.Exists(jarPath))
                throw new FileNotFoundException($"Server JAR not found: {jarPath}");

            string normalizedWorkDir = Path.GetFullPath(workDir);
            string normalizedJarPath = Path.GetFullPath(jarPath);
            if (!normalizedJarPath.StartsWith(normalizedWorkDir + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("JAR path is outside instance directory");

            if (!SecurityHelper.IsValidJvmArgs(info.ExtraJvmArgs))
                throw new InvalidOperationException("Invalid JVM arguments");

            var args = new StringBuilder();
            args.Append($"-Xms{info.MinMemoryMb}M ");
            args.Append($"-Xmx{info.MaxMemoryMb}M ");

            if (!string.IsNullOrWhiteSpace(info.ExtraJvmArgs))
                args.Append($"{info.ExtraJvmArgs.Trim()} ");

            args.Append($"-jar \"{info.ServerJar}\" nogui");

            CreateJobObject();

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = args.ToString(),
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) OutputReceived?.Invoke(this, e.Data);
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) ErrorReceived?.Invoke(this, e.Data);
            };

            _process.Exited += (_, _) =>
            {
                int code = -1;
                try { code = _process.ExitCode; } catch { }
                Exited?.Invoke(this, code);
            };

            _process.Start();

            IntPtr processHandle = _process.Handle;
            if (!AssignProcessToJobObject(_jobHandle, processHandle))
            {
                _process.Kill(entireProcessTree: true);
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
                throw new InvalidOperationException($"Failed to assign process to job object: {GetLastError()}");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public void SendCommand(string command)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Server is not running.");

            if (string.IsNullOrWhiteSpace(command))
                return;

            if (command.Length > 1024)
                command = command[..1024];

            _process!.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }

        public void Stop() => SendCommand("stop");

        public void Kill()
        {
            if (IsRunning)
                _process!.Kill(entireProcessTree: true);
        }

        public void WaitForExit() => _process?.WaitForExit();

        public bool WaitForExit(int milliseconds)
            => _process?.WaitForExit(milliseconds) ?? true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (IsRunning)
            {
                try { _process!.Kill(entireProcessTree: true); } catch { }
            }

            _process?.Dispose();
            _process = null;

            if (_jobHandle != IntPtr.Zero)
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }
    }
}
