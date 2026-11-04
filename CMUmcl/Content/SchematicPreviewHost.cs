// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CMUmcl.Content;

internal sealed class SchematicPreviewHost : HwndHost
{
    private readonly TaskCompletionSource<IntPtr> _created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal event Action? BoundsChanged;
    internal event Action? SurfaceDestroyed;
    internal IntPtr ParentHandle { get; private set; }
    internal SchematicPreviewHost()
    {
        Focusable = true;
        IsVisibleChanged += (_, _) => BoundsChanged?.Invoke();
    }
    internal async Task<IntPtr> WaitForHandleAsync(CancellationToken cancellation)
    {
        var cancelled = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => cancelled.TrySetCanceled());
        return await await Task.WhenAny(_created.Task, cancelled.Task);
    }
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        ParentHandle = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (ParentHandle == IntPtr.Zero) throw new Win32Exception();
        _created.TrySetResult(ParentHandle);
        return new HandleRef(this, ParentHandle);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        SurfaceDestroyed?.Invoke();
        DestroyWindow(hwnd.Handle); ParentHandle = IntPtr.Zero;
    }
    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        BoundsChanged?.Invoke();
    }
    protected override bool TabIntoCore(TraversalRequest request)
    {
        IntPtr child = GetWindow(ParentHandle, 5);
        if (child == IntPtr.Zero) return false;
        SetFocus(child); return true;
    }
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0014) { handled = true; return new IntPtr(1); }
        if (msg == 0x000F)
        {
            IntPtr dc = BeginPaint(hwnd, out PaintData paint);
            IntPtr brush = CreateSolidBrush(0x0016100B);
            try { GetClientRect(hwnd, out NativeRect rect); FillRect(dc, ref rect, brush); }
            finally { DeleteObject(brush); EndPaint(hwnd, ref paint); }
            handled = true; return IntPtr.Zero;
        }
        if (msg == 0x8002)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => MoveFocus(new TraversalRequest(wParam == IntPtr.Zero ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous))));
            return IntPtr.Zero;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintData { public IntPtr Dc; public int Erase; public NativeRect Rect; public int Restore, Update; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved; }
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintData paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PaintData paint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref NativeRect rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
}
