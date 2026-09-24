using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace UIAutomationInspectorWpf.Services;

public sealed class ElementPickerService : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _leftButtonWasDown;

    public ElementPickerService()
    {
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler<AutomationElement>? ElementPicked;

    public bool IsPicking => _timer.IsEnabled;

    public void Start()
    {
        _leftButtonWasDown = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _leftButtonWasDown = false;
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var leftButtonIsDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        if (!_leftButtonWasDown && leftButtonIsDown && GetCursorPos(out var point))
        {
            try
            {
                var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
                ElementPicked?.Invoke(this, element);
            }
            catch (ElementNotAvailableException)
            {
                // The target can disappear while it is being picked.
            }
            finally
            {
                Stop();
            }
        }

        _leftButtonWasDown = leftButtonIsDown;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKeyCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}