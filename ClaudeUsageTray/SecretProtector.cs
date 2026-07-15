// version 7
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeUsageTray;

internal static class SecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;
        public IntPtr DataPointer;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memoryHandle);

    public static string Protect(string plaintext)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(plaintext);
        DataBlob inputBlob = CreateBlob(inputBytes);

        try
        {
            if (!CryptProtectData(
                    ref inputBlob,
                    "Claude Usage Tray Home Assistant token",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out DataBlob outputBlob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                byte[] outputBytes = CopyFromBlob(outputBlob);
                return Convert.ToBase64String(outputBytes);
            }
            finally
            {
                if (outputBlob.DataPointer != IntPtr.Zero)
                {
                    LocalFree(outputBlob.DataPointer);
                }
            }
        }
        finally
        {
            FreeBlob(inputBlob);
        }
    }

    public static string Unprotect(string protectedText)
    {
        byte[] inputBytes = Convert.FromBase64String(protectedText);
        DataBlob inputBlob = CreateBlob(inputBytes);

        try
        {
            if (!CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out DataBlob outputBlob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                byte[] outputBytes = CopyFromBlob(outputBlob);
                return Encoding.UTF8.GetString(outputBytes);
            }
            finally
            {
                if (outputBlob.DataPointer != IntPtr.Zero)
                {
                    LocalFree(outputBlob.DataPointer);
                }
            }
        }
        finally
        {
            FreeBlob(inputBlob);
        }
    }

    private static DataBlob CreateBlob(byte[] data)
    {
        var blob = new DataBlob
        {
            DataLength = data.Length,
            DataPointer = Marshal.AllocHGlobal(data.Length)
        };

        Marshal.Copy(data, 0, blob.DataPointer, data.Length);
        return blob;
    }

    private static byte[] CopyFromBlob(DataBlob blob)
    {
        var data = new byte[blob.DataLength];
        Marshal.Copy(blob.DataPointer, data, 0, blob.DataLength);
        return data;
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.DataPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.DataPointer);
        }
    }
}
