// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CMUmcl.Content;

internal static class SchematicPreviewStorage
{
    // Resolve only the existing launcher data root. User data may have been
    // relocated using a junction; links below components remain forbidden.
    internal static string UnderDataRoot(string dataRoot)
    {
        string root = Path.GetFullPath(dataRoot);
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.Directory) == 0)
                throw new IOException("启动器数据路径不是目录。");
        }
        catch (FileNotFoundException) { return UncreatedRoot(root); }
        catch (DirectoryNotFoundException) { return UncreatedRoot(root); }

        using var handle = CreateFile(root, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("无法读取启动器数据目录。", new Win32Exception(Marshal.GetLastWin32Error()));
        var buffer = new StringBuilder(1024);
        uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity && length < 32768)
        {
            buffer.Capacity = (int)length + 1;
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("无法确定启动器数据目录的实际位置。", new Win32Exception(Marshal.GetLastWin32Error()));
        string physical = buffer.ToString();
        if (physical.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) physical = @"\\" + physical.Substring(8);
        else if (physical.StartsWith(@"\\?\", StringComparison.Ordinal)) physical = physical.Substring(4);
        SchematicPreviewSession.CheckRegularPath(physical);
        return Path.Combine(physical, "components", SchematicPreviewSession.ComponentId);
    }

    private static string UncreatedRoot(string root)
    {
        SchematicPreviewSession.CheckRegularPath(root);
        return Path.Combine(root, "components", SchematicPreviewSession.ComponentId);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
