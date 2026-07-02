// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 管理单个 Minecraft 服务器进程的生命周期，包括启动、停止、命令发送和 RCON 通信。
    /// </summary>
    public partial class ServerProcess(string instanceId) : IDisposable, IAsyncDisposable
    {
        private Process? _process;
        private RconClient? _rconClient;
        private readonly SemaphoreSlim _rconLock = new(1, 1);
        private bool _disposed;
        private IntPtr _jobHandle = IntPtr.Zero;
        private int _processId;
        private bool _startCompleted;

        /// <summary>关联的实例 ID</summary>
        public string InstanceId { get; } = instanceId;

        /// <summary>服务器进程 ID，未启动时为 null</summary>
        public int? ProcessId => _processId > 0 ? _processId : null;

        /// <summary>服务器进程是否正在运行</summary>
        public bool IsRunning
        {
            get
            {
                if (_disposed) return false;
                var p = _process;
                if (p == null) return false;
                try { return !p.HasExited; }
                catch { return false; }
            }
        }

        /// <summary>启动流程是否已完成</summary>
        public bool StartCompleted => _startCompleted;

        /// <summary>服务器标准输出事件</summary>
        public event EventHandler<string>? OutputReceived;
        /// <summary>服务器标准错误输出事件</summary>
        public event EventHandler<string>? ErrorReceived;
        /// <summary>服务器进程退出事件，参数为退出代码</summary>
        public event EventHandler<int>? Exited;

        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

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
            _jobHandle = CreateJobObject(IntPtr.Zero, $"SMSM_{InstanceId}_{Guid.NewGuid():N}");
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

        /// <summary>
        /// 异步启动 Minecraft 服务器进程。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("Server is already running.");

            var info = InstanceManager.GetById(InstanceId)
                ?? throw new InvalidOperationException($"Instance '{InstanceId}' not found.");

            // 检查并自动接受 EULA
            if (ConfigManager.Current.AutoAcceptEula)
            {
                InstanceManager.AcceptEula(InstanceId);
            }

            InstanceManager.EnsureRconConfiguration(InstanceId);

            string javaPath = InstanceManager.ResolveJdkPath(InstanceId);
            if (string.IsNullOrWhiteSpace(javaPath))
                throw new InvalidOperationException("JDK path is not configured.");

            if (!Path.IsPathRooted(javaPath) || !File.Exists(javaPath))
                throw new InvalidOperationException("Configured JDK path is invalid.");

            string workDir = PathHelper.GetInstanceDir(InstanceId);
            string jarPath = PathHelper.GetServerJarPath(InstanceId, info.ServerJar);

            if (!File.Exists(jarPath))
                throw new FileNotFoundException($"Server JAR not found: {jarPath}");

            string normalizedWorkDir = Path.GetFullPath(workDir);
            string normalizedJarPath = Path.GetFullPath(jarPath);
            if (!normalizedJarPath.StartsWith(normalizedWorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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

            _process.Exited += OnProcessExited;

            var started = await Task.Run(() => _process.Start(), cancellationToken);
            if (!started)
            {
                throw new InvalidOperationException("Failed to start Java process.");
            }

            _processId = _process.Id;

            var process = _process ?? throw new InvalidOperationException("Server process was not created.");
            IntPtr processHandle = process.Handle;
            if (!AssignProcessToJobObject(_jobHandle, processHandle))
            {
                process.Kill(entireProcessTree: true);
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
                _processId = 0;
                throw new InvalidOperationException($"Failed to assign process to job object: {GetLastError()}");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _startCompleted = true;
        }

        /// <summary>同步启动服务器进程</summary>
        public void Start()
        {
            StartAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 向服务器标准输入发送控制台命令。
        /// </summary>
        /// <param name="command">要执行的命令</param>
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

        /// <summary>向服务器发送 stop 命令以优雅关闭</summary>
        public void Stop() => SendCommand("stop");

        /// <summary>
        /// 通过 RCON 协议异步执行服务器命令。
        /// </summary>
        /// <param name="command">RCON 命令</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>命令执行结果字符串</returns>
        public async Task<string> ExecuteRconCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Server is not running.");

            await _rconLock.WaitAsync(cancellationToken);
            try
            {
                return await ExecuteRconCommandCoreAsync(command, cancellationToken);
            }
            finally
            {
                _rconLock.Release();
            }
        }

        private async Task<string> ExecuteRconCommandCoreAsync(string command, CancellationToken cancellationToken)
        {
            _rconClient ??= new RconClient(InstanceManager.GetRconConnectionInfo(InstanceId));

            try
            {
                return await _rconClient.ExecuteCommandAsync(command, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                await ResetRconClientAsync();
                _rconClient = new RconClient(InstanceManager.GetRconConnectionInfo(InstanceId));
                return await _rconClient.ExecuteCommandAsync(command, cancellationToken);
            }
        }

        private async ValueTask ResetRconClientAsync()
        {
            if (_rconClient == null)
            {
                return;
            }

            try
            {
                await _rconClient.DisposeAsync();
            }
            catch
            {
            }

            _rconClient = null;
        }

        /// <summary>强制终止服务器进程及其子进程树</summary>
        public void Kill()
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }

        /// <summary>等待服务器进程退出</summary>
        public void WaitForExit() => _process?.WaitForExit();

        /// <summary>
        /// 在指定超时时间内等待服务器进程退出。
        /// </summary>
        /// <param name="milliseconds">超时毫秒数</param>
        /// <returns>进程是否已退出</returns>
        public bool WaitForExit(int milliseconds)
            => _process?.WaitForExit(milliseconds) ?? true;

        /// <summary>释放服务器进程及相关资源</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                ResetRconClientAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
            }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    try { _process.Kill(entireProcessTree: true); } catch { }
                }
            }
            catch { }

            try
            {
                _process?.Dispose();
            }
            catch { }
            _process = null;
            _processId = 0;

            if (_jobHandle != IntPtr.Zero)
            {
                try { CloseHandle(_jobHandle); } catch { }
                _jobHandle = IntPtr.Zero;
            }

            try
            {
                _rconLock.Dispose();
            }
            catch
            {
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>异步释放服务器进程及相关资源</summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                await ResetRconClientAsync();
            }
            catch
            {
            }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    try { _process.Kill(entireProcessTree: true); } catch { }
                }
            }
            catch { }

            try
            {
                _process?.Dispose();
            }
            catch { }
            _process = null;
            _processId = 0;

            if (_jobHandle != IntPtr.Zero)
            {
                try { CloseHandle(_jobHandle); } catch { }
                _jobHandle = IntPtr.Zero;
            }

            try
            {
                _rconLock.Dispose();
            }
            catch
            {
            }

            GC.SuppressFinalize(this);
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            int code = -1;
            try { code = _process?.ExitCode ?? -1; } catch { }
            Exited?.Invoke(this, code);
        }
    }
}
