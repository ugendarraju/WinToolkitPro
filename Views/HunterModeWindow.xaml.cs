using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace WinToolkit.Views;

public partial class HunterModeWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private bool _isDragging;
    private readonly Action<string, string> _onTargetIdentified;

    public HunterModeWindow(Action<string, string> onTargetIdentified)
    {
        InitializeComponent();
        _onTargetIdentified = onTargetIdentified;
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        Mouse.Capture(TargetBadge);
    }

    private void Border_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            if (GetCursorPos(out var p))
            {
                Left = p.X - (Width / 2);
                Top = p.Y - (Height / 2);
            }
        }
    }

    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Mouse.Capture(null);

            if (GetCursorPos(out var p))
            {
                // Find window under cursor
                var hWnd = WindowFromPoint(p);
                if (hWnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hWnd, out var procId);
                    if (procId > 0 && procId != Process.GetCurrentProcess().Id)
                    {
                        try
                        {
                            using var proc = Process.GetProcessById((int)procId);
                            var procName = proc.ProcessName;
                            string procPath = string.Empty;
                            try
                            {
                                procPath = proc.MainModule?.FileName ?? string.Empty;
                            }
                            catch { }

                            _onTargetIdentified?.Invoke(procName, procPath);
                            Close();
                            return;
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
