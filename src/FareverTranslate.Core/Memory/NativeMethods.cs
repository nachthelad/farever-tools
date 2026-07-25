using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FareverTranslate.Core.Memory;

internal static class NativeMethods
{
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint MemCommit = 0x1000;
    internal const uint PageNoAccess = 0x01;
    internal const uint PageGuard = 0x100;
    internal const uint WritableMask = 0x04 | 0x08 | 0x40 | 0x80;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessMemoryHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessMemoryHandle process,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        IntPtr size,
        out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualQueryEx(
        SafeProcessMemoryHandle process,
        IntPtr address,
        out MemoryBasicInformation information,
        IntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        internal IntPtr BaseAddress;
        internal IntPtr AllocationBase;
        internal uint AllocationProtect;
        internal uint Alignment1;
        internal long RegionSize;
        internal uint State;
        internal uint Protect;
        internal uint Type;
        internal uint Alignment2;
    }

    internal sealed class SafeProcessMemoryHandle
        : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeProcessMemoryHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
}

