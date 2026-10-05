using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ImageOptimizer.Core;

/// <summary>
/// A Windows job object that ends the processes in it when it's closed. Windows closes it when the app exits for
/// any reason (closed mid-batch, crashed or ended from Task Manager), so a slow optimizer can't keep running and
/// holding files open after the window has gone.
/// </summary>
internal sealed class ChildProcessJob : IDisposable
{
  /// <summary>The app's own job, kept open for as long as the app runs. Null when not on Windows.</summary>
  public static ChildProcessJob? Shared { get; } = OperatingSystem.IsWindows() ? TryCreate() : null;

  private readonly SafeFileHandle _handle;

  private ChildProcessJob(SafeFileHandle handle)
  {
    _handle = handle;
  }

  internal static ChildProcessJob? TryCreate()
  {
    var handle = CreateJobObject(IntPtr.Zero, null);
    if (handle.IsInvalid)
    {
      handle.Dispose();
      return null;
    }

    var limits = new JobObjectExtendedLimitInformation
    {
      BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
    };
    if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, ref limits, Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
    {
      handle.Dispose();
      return null;
    }
    return new ChildProcessJob(handle);
  }

  /// <summary>Adds a started process. False if Windows wouldn't add it (it then runs as it would without a job).</summary>
  public bool TryAdd(Process process)
  {
    try
    {
      return AssignProcessToJobObject(_handle, process.Handle);
    }
    catch (InvalidOperationException)
    {
      // The process has already exited.
      return false;
    }
  }

  public void Dispose() => _handle.Dispose();

  private const int JobObjectExtendedLimitInformationClass = 9;
  private const uint JobObjectLimitKillOnJobClose = 0x2000;

  [StructLayout(LayoutKind.Sequential)]
  private struct JobObjectBasicLimitInformation
  {
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public UIntPtr MinimumWorkingSetSize;
    public UIntPtr MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public UIntPtr Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct IoCounters
  {
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct JobObjectExtendedLimitInformation
  {
    public JobObjectBasicLimitInformation BasicLimitInformation;
    public IoCounters IoInfo;
    public UIntPtr ProcessMemoryLimit;
    public UIntPtr JobMemoryLimit;
    public UIntPtr PeakProcessMemoryUsed;
    public UIntPtr PeakJobMemoryUsed;
  }

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, int length);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
