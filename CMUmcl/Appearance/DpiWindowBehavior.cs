using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CMUmcl.Appearance;

// Preserve the page design while keeping the top-level window inside the monitor work area.
internal static class DpiWindowBehavior
{
    private sealed class State(Window window)
    {
        internal readonly double MinWidth=window.MinWidth,MinHeight=window.MinHeight;
        internal readonly double MaxWidth=window.MaxWidth,MaxHeight=window.MaxHeight;
        internal bool Hooked;
        internal bool Pending;
    }
    private static readonly ConditionalWeakTable<Window,State> States=new();
    private static bool _initialized;
    internal static void Initialize()
    {
        if(_initialized)return;_initialized=true;
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,args)=>
        {
            if(sender is not Window window || args.OriginalSource!=window)return;
            var state=States.GetValue(window,w=>new State(w));
            var handle=new WindowInteropHelper(window).Handle;
            if(!state.Hooked && HwndSource.FromHwnd(handle) is HwndSource source)
            {
                state.Hooked=true;
                window.ContentRendered+=(_,_)=>ScheduleFit(window,state);
                source.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>
                {
                    if(message==0x0024 && window.WindowStyle==WindowStyle.None)
                    {
                        var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
                        if(GetMonitorInfo(monitor,ref info))
                        {
                            var limits=Marshal.PtrToStructure<MinMaxInfo>(lParam);
                            limits.MaxPosition=new PointI{X=info.Work.Left-info.Monitor.Left,Y=info.Work.Top-info.Monitor.Top};
                            limits.MaxSize=new PointI{X=info.Work.Right-info.Work.Left,Y=info.Work.Bottom-info.Work.Top};
                            Marshal.StructureToPtr(limits,lParam,false);handled=true;
                        }
                    }
                    if(message==0x02E0 || message==0x007E)
                        ScheduleFit(window,state);
                    return IntPtr.Zero;
                });
            }
            FitCurrentMonitor(window);
            ScheduleFit(window,state);
        }));
    }
    private static void ScheduleFit(Window window,State state)
    {
        if(state.Pending)return;
        state.Pending=true;
        // WPF applies centering and the suggested DPI rectangle after Loaded.
        // Wait for layout before constraining the final native window position.
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
        {
            state.Pending=false;
            if(window.IsLoaded)FitCurrentMonitor(window);
        }));
    }
    internal static void FitCurrentMonitor(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        try
        {
            var monitor=MonitorFromWindow(handle,2);var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
            if(monitor!=IntPtr.Zero && GetMonitorInfo(monitor,ref info))
            {
                uint dpi=GetDpiForWindow(handle);if(dpi==0)dpi=96;
                Fit(window,(info.Work.Right-info.Work.Left)*96d/dpi,(info.Work.Bottom-info.Work.Top)*96d/dpi);
                if(window.WindowState==WindowState.Normal && GetWindowRect(handle,out var rect))
                {
                    int workWidth=info.Work.Right-info.Work.Left;
                    int workHeight=info.Work.Bottom-info.Work.Top;
                    int outerWidth=rect.Right-rect.Left;
                    int outerHeight=rect.Bottom-rect.Top;
                    if(outerWidth>workWidth || outerHeight>workHeight)
                    {
                        // WPF Width/Height are client/layout units; the native frame
                        // can be a few pixels larger because of the resize border.
                        // Clamp the real outer frame after WPF has applied its size.
                        SetWindowPos(handle,IntPtr.Zero,0,0,
                            Math.Min(outerWidth,workWidth),Math.Min(outerHeight,workHeight),
                            0x0002|0x0004|0x0010);
                        GetWindowRect(handle,out rect);
                    }
                    int left=ConstrainPosition(rect.Left,rect.Right-rect.Left,info.Work.Left,info.Work.Right);
                    int top=ConstrainPosition(rect.Top,rect.Bottom-rect.Top,info.Work.Top,info.Work.Bottom);
                    if(left!=rect.Left || top!=rect.Top)
                        SetWindowPos(handle,IntPtr.Zero,left,top,0,0,0x0001|0x0004|0x0010);
                }
                return;
            }
        }
        catch(EntryPointNotFoundException) { }
        Fit(window,SystemParameters.WorkArea.Width,SystemParameters.WorkArea.Height);
    }
    internal static int ConstrainPosition(int position,int extent,int start,int end)
        =>Math.Max(start,Math.Min(position,end-extent));
    internal static void Fit(Window window,double availableWidth,double availableHeight)
    {
        if(availableWidth<=0 || availableHeight<=0)return;
        var state=States.GetValue(window,w=>new State(w));
        window.MinWidth=Math.Min(state.MinWidth,availableWidth);
        window.MinHeight=Math.Min(state.MinHeight,availableHeight);
        window.MaxWidth=window.WindowStyle==WindowStyle.None?state.MaxWidth:Math.Min(state.MaxWidth,availableWidth);
        window.MaxHeight=window.WindowStyle==WindowStyle.None?state.MaxHeight:Math.Min(state.MaxHeight,availableHeight);
        if(window.WindowState==WindowState.Normal)
        {
            if(!double.IsNaN(window.Width))window.Width=Math.Min(window.Width,availableWidth);
            if(!double.IsNaN(window.Height))window.Height=Math.Min(window.Height,availableHeight);
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct PointI {internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MinMaxInfo {internal PointI Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect {internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo {internal int Size;internal Rect Monitor,Work;internal uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
}
