using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AppCapture.Core.Timing;

/// <summary>高分解能 waitable timer で一定間隔に待つ（Thread.Sleep の 15.6ms 粒度を避ける）</summary>
public sealed class FramePacer : IDisposable
{
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x1F0003;

    private readonly nint _timer;
    private readonly long _intervalTicks;
    private long _next;

    public FramePacer(double fps)
    {
        _intervalTicks = (long)(Stopwatch.Frequency / fps);
        _timer = CreateWaitableTimerExW(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (_timer == 0) throw new InvalidOperationException("high resolution waitable timer is not available");
        _next = Stopwatch.GetTimestamp() + _intervalTicks;
    }

    /// <summary>次のフレーム時刻まで待つ。大きく遅れた場合は追いつこうとせず基準をリセットする</summary>
    public void Wait()
    {
        long now = Stopwatch.GetTimestamp();
        long remaining = _next - now;
        if (remaining > 0)
        {
            long dueTime100ns = -(remaining * 10_000_000 / Stopwatch.Frequency); // 負値 = 相対時間
            if (SetWaitableTimer(_timer, in dueTime100ns, 0, 0, 0, false))
                WaitForSingleObject(_timer, 1000);
            _next += _intervalTicks;
        }
        else
        {
            _next = (remaining < -_intervalTicks ? now : _next) + _intervalTicks;
        }
    }

    public void Dispose() => CloseHandle(_timer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint timer, in long dueTime, int period, nint completion, nint arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint ms);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
