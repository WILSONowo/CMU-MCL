// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CMUmcl.Content;

internal sealed class PreviewProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal PreviewProcessJob(IntPtr process)
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new Win32Exception();
        try
        {
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 | 0x100 }, ProcessMemory = new UIntPtr(2UL * 1024 * 1024 * 1024) };
            if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))) || !AssignProcessToJobObject(_handle, process)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法隔离预览进程。");
        }
        catch { _handle.Dispose(); throw; }
    }
    public void Dispose() => _handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { internal long ProcessTime, JobTime; internal uint Flags; internal UIntPtr MinimumWorkingSet, MaximumWorkingSet; internal uint ActiveProcesses; internal UIntPtr Affinity; internal uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { internal BasicLimits Basic; internal IoCounters Io; internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
