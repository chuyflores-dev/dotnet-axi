using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using DotNetAxi.Contracts;
using Microsoft.Win32.SafeHandles;

namespace DotNetAxi.DotNet;

internal sealed class SystemContainedProcessFactory : IContainedProcessFactory
{
    public IContainedProcess? Start(ProcessStartInfo startInfo) =>
        OperatingSystem.IsWindows()
            ? WindowsJobProcess.Start(startInfo)
            : PosixProcessAuthority.Start(startInfo);
}

internal static class PosixProcessAuthority
{
    // macOS models both spawn types as opaque pointers; glibc models them as
    // opaque value structs. The largest current glibc representation is well
    // below 1 KiB. Supplying an oversized, aligned native buffer is valid for
    // both ABIs and avoids exposing either platform's private layout.
    private const int OpaqueSpawnBufferSize = 1024;
    private const short PosixSpawnSetProcessGroup = 0x02;
    private const int StandardInput = 0;
    private const int StandardOutput = 1;
    private const int StandardError = 2;
    private const int ReadOnly = 0;
    private const int ExecuteAccess = 1;
    private const int SignalKill = 9;
    private const int OperationNotPermitted = 1;
    private const int Interrupted = 4;
    private const int NoSuchProcess = 3;
    private const int ProcessIdType = 1;
    private const int WaitExited = 0x00000004;
    private const int MacOsWaitNoWait = 0x00000020;
    private const int LinuxWaitNoWait = 0x01000000;
    private const uint MacOsProcessGroupOnly = 2;

    internal static bool UsesSuperUserExecutionSemantics =>
        !OperatingSystem.IsWindows()
        && NativeMethods.GetEffectiveUserId() == 0;

    public static bool CanExecute(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        var directory = OperatingSystem.IsMacOS() ? -2 : -100;
        var effectiveAccess = OperatingSystem.IsMacOS() ? 0x0010 : 0x0200;
        return NativeMethods.FileAccessAt(
            directory,
            path,
            ExecuteAccess,
            effectiveAccess) == 0;
    }

    public static int GetEffectiveAccessError(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "Effective POSIX access checks are supported on Linux and macOS.");
        }

        var directory = OperatingSystem.IsMacOS() ? -2 : -100;
        var effectiveAccess = OperatingSystem.IsMacOS() ? 0x0010 : 0x0200;
        return NativeMethods.FileAccessAt(
            directory,
            path,
            ExecuteAccess,
            effectiveAccess) == 0
            ? 0
            : Marshal.GetLastPInvokeError();
    }

    public static IContainedProcess? Start(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "Contained POSIX process launch is supported on Linux and macOS.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.FileName);
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError
            || startInfo.RedirectStandardInput)
        {
            throw new InvalidOperationException(
                "The contained host requires direct execution with stdout "
                + "and stderr redirection only.");
        }

        using var standardOutput = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        using var standardError = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        var outputWrite = ParseFileDescriptor(
            standardOutput.GetClientHandleAsString());
        var errorWrite = ParseFileDescriptor(
            standardError.GetClientHandleAsString());
        var outputRead = standardOutput.SafePipeHandle.DangerousGetHandle().ToInt32();
        var errorRead = standardError.SafePipeHandle.DangerousGetHandle().ToInt32();
        var actions = IntPtr.Zero;
        var attributes = IntPtr.Zero;
        var arguments = IntPtr.Zero;
        var environment = IntPtr.Zero;
        var actionsInitialized = false;
        var attributesInitialized = false;
        var childPid = 0;

        try
        {
            actions = AllocateNativeBuffer();
            attributes = AllocateNativeBuffer();
            CheckPosix(NativeMethods.SpawnFileActionsInit(actions));
            actionsInitialized = true;
            CheckPosix(NativeMethods.SpawnFileActionsAddOpen(
                actions,
                StandardInput,
                "/dev/null",
                ReadOnly,
                0));
            CheckPosix(NativeMethods.SpawnFileActionsAddDup2(
                actions,
                outputWrite,
                StandardOutput));
            CheckPosix(NativeMethods.SpawnFileActionsAddDup2(
                actions,
                errorWrite,
                StandardError));
            AddCloseAction(actions, outputRead);
            AddCloseAction(actions, errorRead);
            AddCloseAction(actions, outputWrite);
            AddCloseAction(actions, errorWrite);
            CheckPosix(NativeMethods.SpawnFileActionsAddChangeDirectory(
                actions,
                startInfo.WorkingDirectory));

            CheckPosix(NativeMethods.SpawnAttributesInit(attributes));
            attributesInitialized = true;
            CheckPosix(NativeMethods.SpawnAttributesSetProcessGroup(
                attributes,
                0));
            CheckPosix(NativeMethods.SpawnAttributesSetFlags(
                attributes,
                PosixSpawnSetProcessGroup));

            arguments = AllocateStringArray(
                [startInfo.FileName, .. startInfo.ArgumentList]);
            environment = AllocateStringArray(startInfo.Environment
                .Where(static pair => pair.Value is not null)
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}={pair.Value}")
                .ToArray());
            var result = NativeMethods.Spawn(
                out childPid,
                startInfo.FileName,
                actions,
                attributes,
                arguments,
                environment);
            CheckPosix(result);

            standardOutput.DisposeLocalCopyOfClientHandle();
            standardError.DisposeLocalCopyOfClientHandle();
            return new PosixContainedProcess(
                childPid,
                new StreamReader(
                    TransferPipeOwnership(standardOutput),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true),
                new StreamReader(
                    TransferPipeOwnership(standardError),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true));
        }
        catch
        {
            if (childPid > 0)
            {
                _ = NativeMethods.Kill(-childPid, SignalKill);
                _ = WaitForProcess(childPid);
            }

            throw;
        }
        finally
        {
            if (attributesInitialized)
            {
                _ = NativeMethods.SpawnAttributesDestroy(attributes);
            }

            if (actionsInitialized)
            {
                _ = NativeMethods.SpawnFileActionsDestroy(actions);
            }

            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(actions);
            FreeStringArray(environment);
            FreeStringArray(arguments);
        }
    }

    private static FileStream TransferPipeOwnership(
        AnonymousPipeServerStream pipe)
    {
        var handle = new SafeFileHandle(
            pipe.SafePipeHandle.DangerousGetHandle(),
            ownsHandle: true);
        pipe.SafePipeHandle.SetHandleAsInvalid();
        return new FileStream(
            handle,
            FileAccess.Read,
            bufferSize: 4096,
            isAsync: false);
    }

    private static int ParseFileDescriptor(string value) =>
        int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    internal static bool ShouldCloseChildFileDescriptor(int fileDescriptor) =>
        fileDescriptor > StandardError;

    private static void AddCloseAction(IntPtr actions, int fileDescriptor)
    {
        if (ShouldCloseChildFileDescriptor(fileDescriptor))
        {
            CheckPosix(NativeMethods.SpawnFileActionsAddClose(
                actions,
                fileDescriptor));
        }
    }

    private static IntPtr AllocateNativeBuffer()
    {
        var buffer = Marshal.AllocHGlobal(OpaqueSpawnBufferSize);
        try
        {
            Marshal.Copy(
                new byte[OpaqueSpawnBufferSize],
                0,
                buffer,
                OpaqueSpawnBufferSize);
            return buffer;
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            throw;
        }
    }

    private static IntPtr AllocateStringArray(IReadOnlyList<string> values)
    {
        var array = Marshal.AllocHGlobal(
            checked((values.Count + 1) * IntPtr.Size));
        for (var index = 0; index <= values.Count; index++)
        {
            Marshal.WriteIntPtr(array, index * IntPtr.Size, IntPtr.Zero);
        }

        try
        {
            for (var index = 0; index < values.Count; index++)
            {
                Marshal.WriteIntPtr(
                    array,
                    index * IntPtr.Size,
                    Marshal.StringToCoTaskMemUTF8(values[index]));
            }

            return array;
        }
        catch
        {
            FreeStringArray(array);
            throw;
        }
    }

    private static void FreeStringArray(IntPtr array)
    {
        if (array == IntPtr.Zero)
        {
            return;
        }

        for (var offset = 0; ; offset += IntPtr.Size)
        {
            var value = Marshal.ReadIntPtr(array, offset);
            if (value == IntPtr.Zero)
            {
                break;
            }

            Marshal.FreeCoTaskMem(value);
        }

        Marshal.FreeHGlobal(array);
    }

    private static void CheckPosix(int result)
    {
        if (result != 0)
        {
            throw new Win32Exception(result);
        }
    }

    private static ProcessExitEvidence WaitForProcess(int processId)
    {
        while (true)
        {
            var result = NativeMethods.WaitProcess(
                processId,
                out var status,
                0);
            if (result == processId)
            {
                return CreateExitEvidence(status);
            }

            if (result < 0 && Marshal.GetLastPInvokeError() == Interrupted)
            {
                continue;
            }

            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static void WaitForProcessExitWithoutReaping(int processId)
    {
        var information = AllocateNativeBuffer();
        try
        {
            while (true)
            {
                var result = NativeMethods.WaitForProcessState(
                    ProcessIdType,
                    checked((uint)processId),
                    information,
                    WaitExited | (OperatingSystem.IsLinux()
                        ? LinuxWaitNoWait
                        : MacOsWaitNoWait));
                if (result == 0)
                {
                    return;
                }

                var error = Marshal.GetLastPInvokeError();
                if (error != Interrupted)
                {
                    throw new Win32Exception(error);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    private static void TerminateProcessGroup(int processGroupId)
    {
        var result = NativeMethods.Kill(-processGroupId, SignalKill);
        var error = Marshal.GetLastPInvokeError();
        if (result != 0
            && error != NoSuchProcess
            && (!OperatingSystem.IsMacOS()
                || error != OperationNotPermitted
                || MacOsGroupHasOtherMembers(processGroupId)))
        {
            throw new Win32Exception(error);
        }
    }

    private static void WaitForProcessGroupExit(int processGroupId)
    {
        while (OperatingSystem.IsMacOS()
                   ? MacOsGroupHasOtherMembers(processGroupId)
                   : LinuxGroupHasOtherMembers(processGroupId))
        {
            Thread.Sleep(10);
        }
    }

    private static bool MacOsGroupHasOtherMembers(int processGroupId)
    {
        var requiredBytes = NativeMethods.ListProcesses(
            MacOsProcessGroupOnly,
            checked((uint)processGroupId),
            IntPtr.Zero,
            0);
        if (requiredBytes < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (requiredBytes == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(requiredBytes);
        try
        {
            var writtenBytes = NativeMethods.ListProcesses(
                MacOsProcessGroupOnly,
                checked((uint)processGroupId),
                buffer,
                requiredBytes);
            if (writtenBytes < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            for (var offset = 0;
                 offset + sizeof(int) <= writtenBytes;
                 offset += sizeof(int))
            {
                var processId = Marshal.ReadInt32(buffer, offset);
                if (processId != 0 && processId != processGroupId)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool LinuxGroupHasOtherMembers(int processGroupId)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(
                    Path.GetFileName(directory),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var processId)
                || processId == processGroupId)
            {
                continue;
            }

            try
            {
                var status = File.ReadAllText(Path.Combine(directory, "stat"));
                var commandEnd = status.LastIndexOf(')');
                if (commandEnd < 0 || commandEnd + 2 >= status.Length)
                {
                    continue;
                }

                var fields = status[(commandEnd + 2)..].Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > 2
                    && int.TryParse(
                        fields[2],
                        System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var groupId)
                    && groupId == processGroupId)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // The process exited between enumeration and inspection.
            }
            catch (UnauthorizedAccessException)
            {
                // Same-user descendants remain inspectable on supported Linux
                // hosts; an unrelated protected process cannot join this group.
            }
        }

        return false;
    }

    private static ProcessExitEvidence CreateExitEvidence(int status)
    {
        var signal = status & 0x7f;
        return signal == 0
            ? new ProcessExitEvidence((status >> 8) & 0xff, null)
            : new ProcessExitEvidence(null, signal);
    }

    private sealed class PosixContainedProcess : IContainedProcess
    {
        private readonly PosixOwnedProcessGroup _processGroup;
        private readonly Task<ProcessExitEvidence> _exit;

        public PosixContainedProcess(
            int processId,
            TextReader standardOutput,
            TextReader standardError)
        {
            _processGroup = new PosixOwnedProcessGroup(
                processId,
                WaitForProcessExitWithoutReaping,
                WaitForProcess,
                TerminateProcessGroup,
                WaitForProcessGroupExit);
            StandardOutput = standardOutput;
            StandardError = standardError;
            _exit = Task.Run(_processGroup.WaitForExitAndContainDescendants);
        }

        public TextReader StandardOutput { get; }

        public TextReader StandardError { get; }

        public ProcessExitEvidence ExitEvidence => _processGroup.ExitEvidence;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.WaitAsync(cancellationToken);

        public void TerminateTree() => _processGroup.Terminate();

        public void Dispose()
        {
            try
            {
                TerminateTree();
                _ = _exit.GetAwaiter().GetResult();
            }
            finally
            {
                StandardOutput.Dispose();
                StandardError.Dispose();
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "faccessat", SetLastError = true)]
        internal static extern int FileAccessAt(
            int directory,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int mode,
            int flags);

        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
        internal static extern int SpawnFileActionsInit(IntPtr actions);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
        internal static extern int SpawnFileActionsDestroy(IntPtr actions);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen")]
        internal static extern int SpawnFileActionsAddOpen(
            IntPtr actions,
            int fileDescriptor,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int openFlags,
            int mode);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
        internal static extern int SpawnFileActionsAddDup2(
            IntPtr actions,
            int fileDescriptor,
            int newFileDescriptor);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
        internal static extern int SpawnFileActionsAddClose(
            IntPtr actions,
            int fileDescriptor);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np")]
        internal static extern int SpawnFileActionsAddChangeDirectory(
            IntPtr actions,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string directory);

        [DllImport("libc", EntryPoint = "posix_spawnattr_init")]
        internal static extern int SpawnAttributesInit(IntPtr attributes);

        [DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
        internal static extern int SpawnAttributesDestroy(IntPtr attributes);

        [DllImport("libc", EntryPoint = "posix_spawnattr_setpgroup")]
        internal static extern int SpawnAttributesSetProcessGroup(
            IntPtr attributes,
            int processGroup);

        [DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
        internal static extern int SpawnAttributesSetFlags(
            IntPtr attributes,
            short flags);

        [DllImport("libc", EntryPoint = "posix_spawn")]
        internal static extern int Spawn(
            out int processId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            IntPtr actions,
            IntPtr attributes,
            IntPtr arguments,
            IntPtr environment);

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int processId, int signal);

        [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        internal static extern int WaitProcess(
            int processId,
            out int status,
            int options);

        [DllImport("libc", EntryPoint = "waitid", SetLastError = true)]
        internal static extern int WaitForProcessState(
            int idType,
            uint id,
            IntPtr information,
            int options);

        [DllImport("libproc", EntryPoint = "proc_listpids", SetLastError = true)]
        internal static extern int ListProcesses(
            uint type,
            uint typeInformation,
            IntPtr buffer,
            int bufferSize);
    }
}

internal sealed class PosixOwnedProcessGroup(
    int processGroupId,
    Action<int> waitForLeaderExitWithoutReaping,
    Func<int, ProcessExitEvidence> reapLeader,
    Action<int> terminateGroup,
    Action<int> waitForGroupExit)
{
    private readonly object _gate = new();
    private bool _terminationAuthorityReleased;
    private ProcessExitEvidence? _exitEvidence;

    public ProcessExitEvidence ExitEvidence
    {
        get
        {
            lock (_gate)
            {
                return _exitEvidence
                    ?? throw new InvalidOperationException(
                        "The process exit evidence was not captured.");
            }
        }
    }

    public ProcessExitEvidence WaitForExitAndContainDescendants()
    {
        waitForLeaderExitWithoutReaping(processGroupId);
        try
        {
            lock (_gate)
            {
                if (_terminationAuthorityReleased)
                {
                    return ExitEvidence;
                }

                terminateGroup(processGroupId);
            }

            waitForGroupExit(processGroupId);
        }
        finally
        {
            lock (_gate)
            {
                if (!_terminationAuthorityReleased)
                {
                    _terminationAuthorityReleased = true;
                    _exitEvidence = reapLeader(processGroupId);
                }
            }
        }

        lock (_gate)
        {
            return ExitEvidence;
        }
    }

    public void Terminate()
    {
        lock (_gate)
        {
            if (!_terminationAuthorityReleased)
            {
                terminateGroup(processGroupId);
            }
        }
    }
}

internal static class WindowsJobProcess
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartSuspended = 0x00000004;
    private const uint StartUseStandardHandles = 0x00000100;
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const uint HandleFlagInherit = 0x00000001;
    private const nuint ProcessThreadAttributeHandleList = 0x00020002;
    private const nuint ProcessThreadAttributeJobList = 0x0002000D;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectBasicAccountingInformationClass = 1;
    private const int JobObjectExtendedLimitInformationClass = 9;

    public static IContainedProcess? Start(ProcessStartInfo startInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.FileName);
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError
            || startInfo.RedirectStandardInput)
        {
            throw new InvalidOperationException(
                "The contained host requires direct execution with stdout "
                + "and stderr redirection only.");
        }

        var security = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        CheckWindows(NativeMethods.CreatePipe(
            out var outputRead,
            out var outputWrite,
            ref security,
            0));
        SafeFileHandle? errorRead = null;
        SafeFileHandle? errorWrite = null;
        SafeFileHandle? nullInput = null;
        SafeJobHandle? job = null;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        IntPtr jobList = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        ProcessInformation processInformation = default;
        Process? process = null;

        try
        {
            CheckWindows(NativeMethods.SetHandleInformation(
                outputRead,
                HandleFlagInherit,
                0));
            CheckWindows(NativeMethods.CreatePipe(
                out errorRead,
                out errorWrite,
                ref security,
                0));
            CheckWindows(NativeMethods.SetHandleInformation(
                errorRead,
                HandleFlagInherit,
                0));
            nullInput = NativeMethods.CreateFile(
                "NUL",
                GenericRead,
                0,
                ref security,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (nullInput.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };
            CheckWindows(NativeMethods.SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformationClass,
                ref limits,
                Marshal.SizeOf<JobObjectExtendedLimitInformation>()));

            nuint attributeSize = 0;
            _ = NativeMethods.InitializeProcThreadAttributeList(
                IntPtr.Zero,
                2,
                0,
                ref attributeSize);
            attributeList = Marshal.AllocHGlobal(checked((nint)attributeSize));
            CheckWindows(NativeMethods.InitializeProcThreadAttributeList(
                attributeList,
                2,
                0,
                ref attributeSize));

            var inheritedHandles = new[]
            {
                nullInput.DangerousGetHandle(),
                outputWrite.DangerousGetHandle(),
                errorWrite.DangerousGetHandle(),
            };
            handleList = Marshal.AllocHGlobal(inheritedHandles.Length * IntPtr.Size);
            Marshal.Copy(inheritedHandles, 0, handleList, inheritedHandles.Length);
            CheckWindows(NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                ProcessThreadAttributeHandleList,
                handleList,
                (nuint)(inheritedHandles.Length * IntPtr.Size),
                IntPtr.Zero,
                IntPtr.Zero));

            jobList = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(jobList, job.DangerousGetHandle());
            CheckWindows(NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                ProcessThreadAttributeJobList,
                jobList,
                (nuint)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero));

            var startup = new StartupInfoExtended
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoExtended>(),
                    Flags = StartUseStandardHandles,
                    StandardInput = nullInput.DangerousGetHandle(),
                    StandardOutput = outputWrite.DangerousGetHandle(),
                    StandardError = errorWrite.DangerousGetHandle(),
                },
                AttributeList = attributeList,
            };
            environment = Marshal.StringToHGlobalUni(EnvironmentBlock(startInfo));
            var commandLine = new StringBuilder(CommandLine(startInfo));
            CheckWindows(NativeMethods.CreateProcess(
                startInfo.FileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: true,
                CreateNoWindow
                | CreateUnicodeEnvironment
                | ExtendedStartupInfoPresent
                | StartSuspended,
                environment,
                startInfo.WorkingDirectory,
                ref startup,
                out processInformation));

            process = Process.GetProcessById(processInformation.ProcessId);
            _ = process.SafeHandle;
            if (NativeMethods.ResumeThread(processInformation.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            outputWrite.Dispose();
            errorWrite.Dispose();
            nullInput.Dispose();
            var containedProcess = new WindowsContainedProcess(
                process,
                job,
                new StreamReader(
                    new FileStream(outputRead, FileAccess.Read, 4096, false),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true),
                new StreamReader(
                    new FileStream(errorRead, FileAccess.Read, 4096, false),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true));
            outputRead = null!;
            errorRead = null;
            return containedProcess;
        }
        catch
        {
            process?.Dispose();
            job?.Dispose();
            throw;
        }
        finally
        {
            if (processInformation.Thread != IntPtr.Zero)
            {
                _ = NativeMethods.CloseHandle(processInformation.Thread);
            }

            if (processInformation.Process != IntPtr.Zero)
            {
                _ = NativeMethods.CloseHandle(processInformation.Process);
            }

            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
            }

            Marshal.FreeHGlobal(environment);
            Marshal.FreeHGlobal(jobList);
            Marshal.FreeHGlobal(handleList);
            Marshal.FreeHGlobal(attributeList);
            nullInput?.Dispose();
            errorWrite?.Dispose();
            errorRead?.Dispose();
            outputWrite.Dispose();
            outputRead?.Dispose();
        }
    }

    private static string EnvironmentBlock(ProcessStartInfo startInfo) =>
        string.Join(
            '\0',
            startInfo.Environment
                .Where(static pair => pair.Value is not null)
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}"))
        + "\0\0";

    private static string CommandLine(ProcessStartInfo startInfo) =>
        string.Join(
            ' ',
            new[] { startInfo.FileName }
                .Concat(startInfo.ArgumentList)
                .Select(QuoteWindowsArgument));

    internal static string QuoteWindowsArgument(string value)
    {
        if (value.Length > 0
            && !value.Any(static character =>
                char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        var result = new StringBuilder(value.Length + 2).Append('"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (slashes * 2) + 1).Append('"');
                slashes = 0;
                continue;
            }

            result.Append('\\', slashes).Append(character);
            slashes = 0;
        }

        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void CheckWindows(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private sealed class WindowsContainedProcess : IContainedProcess
    {
        private readonly Process _process;
        private readonly SafeJobHandle _job;
        private readonly Task<ProcessExitEvidence> _exit;
        private ProcessExitEvidence? _exitEvidence;

        public WindowsContainedProcess(
            Process process,
            SafeJobHandle job,
            TextReader standardOutput,
            TextReader standardError)
        {
            _process = process;
            _job = job;
            StandardOutput = standardOutput;
            StandardError = standardError;
            _exit = WaitForExitAndContainDescendantsAsync();
        }

        public TextReader StandardOutput { get; }

        public TextReader StandardError { get; }

        public ProcessExitEvidence ExitEvidence =>
            Volatile.Read(ref _exitEvidence)
            ?? _exit.GetAwaiter().GetResult();

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.WaitAsync(cancellationToken);

        public void TerminateTree()
        {
            if (ActiveProcessCount() == 0)
            {
                return;
            }

            if (!NativeMethods.TerminateJobObject(_job, uint.MaxValue))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        public void Dispose()
        {
            _job.Dispose();
            StandardOutput.Dispose();
            StandardError.Dispose();
            _process.Dispose();
        }

        private async Task<ProcessExitEvidence>
            WaitForExitAndContainDescendantsAsync()
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            var evidence = new ProcessExitEvidence(_process.ExitCode, null);
            Volatile.Write(ref _exitEvidence, evidence);
            TerminateTree();
            while (ActiveProcessCount() != 0)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            return evidence;
        }

        private uint ActiveProcessCount()
        {
            CheckWindows(NativeMethods.QueryInformationJobObject(
                _job,
                JobObjectBasicAccountingInformationClass,
                out var information,
                Marshal.SizeOf<JobObjectBasicAccountingInformation>(),
                IntPtr.Zero));
            return information.ActiveProcesses;
        }
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] internal bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal uint X;
        internal uint Y;
        internal uint XSize;
        internal uint YSize;
        internal uint XCountChars;
        internal uint YCountChars;
        internal uint FillAttribute;
        internal uint Flags;
        internal short ShowWindow;
        internal short Reserved2;
        internal IntPtr ReservedPointer;
        internal IntPtr StandardInput;
        internal IntPtr StandardOutput;
        internal IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoExtended
    {
        internal StartupInfo StartupInfo;
        internal IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal IntPtr Process;
        internal IntPtr Thread;
        internal int ProcessId;
        internal int ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        internal long TotalUserTime;
        internal long TotalKernelTime;
        internal long ThisPeriodTotalUserTime;
        internal long ThisPeriodTotalKernelTime;
        internal uint TotalPageFaultCount;
        internal uint TotalProcesses;
        internal uint ActiveProcesses;
        internal uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize;
        internal nuint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal nuint ProcessMemoryLimit;
        internal nuint JobMemoryLimit;
        internal nuint PeakProcessMemoryUsed;
        internal nuint PeakJobMemoryUsed;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(
            out SafeFileHandle readPipe,
            out SafeFileHandle writePipe,
            ref SecurityAttributes pipeAttributes,
            int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetHandleInformation(
            SafeHandle handle,
            uint mask,
            uint flags);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            ref SecurityAttributes securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateJobObjectW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern SafeJobHandle CreateJobObject(
            IntPtr jobAttributes,
            string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            int informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            out JobObjectBasicAccountingInformation information,
            int informationLength,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(
            IntPtr attributeList,
            int attributeCount,
            int flags,
            ref nuint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            nuint attribute,
            IntPtr value,
            nuint size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        internal static extern void DeleteProcThreadAttributeList(
            IntPtr attributeList);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateProcessW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfoExtended startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(
            SafeJobHandle job,
            uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
