using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FareverTranslate.Core.Security;

public static class DpapiProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("FareverTranslate:v1");

    public static byte[] Protect(byte[] plaintext) =>
        Transform(plaintext, protect: true);

    public static byte[] Unprotect(byte[] ciphertext) =>
        Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            return Array.Empty<byte>();
        }

        GCHandle inputHandle = default;
        GCHandle entropyHandle = default;
        DataBlob output = default;
        try
        {
            inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
            var inputBlob = new DataBlob(input.Length, inputHandle.AddrOfPinnedObject());
            var entropyBlob =
                new DataBlob(Entropy.Length, entropyHandle.AddrOfPinnedObject());

            bool success = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!success)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    protect ? "No se pudo proteger el dato." : "No se pudo descifrar el dato.");
            }

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
            if (inputHandle.IsAllocated)
            {
                inputHandle.Free();
            }
            if (entropyHandle.IsAllocated)
            {
                entropyHandle.Free();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob
    {
        internal DataBlob(int length, IntPtr data)
        {
            Length = length;
            Data = data;
        }

        internal int Length { get; }
        internal IntPtr Data { get; }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

