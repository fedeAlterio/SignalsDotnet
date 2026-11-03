using SignalsDotnet.Internals;

namespace SignalsDotnet;

public readonly struct SignalClock(TimeProvider timeProvider)
{
    public static SignalClock System => new(TimeProvider.System);

    public TimeProvider TimeProvider => timeProvider ?? TimeProvider.System;

    internal bool IsBefore(DateTimeOffset instant)
    {
        var now = TimeProvider.GetUtcNow();
        if (now >= instant)
            return false;

        if (Signal.InsideComputed)
            Signal.NotifySignalRequested(new DeadlineSignal(instant - now, TimeProvider));

        return true;
    }

    internal bool IsAfter(DateTimeOffset instant) => !IsBefore(instant == DateTimeOffset.MaxValue ? instant : instant.AddTicks(1));

    public static bool operator <(SignalClock now, DateTimeOffset instant) => now.IsBefore(instant);
    public static bool operator >=(SignalClock now, DateTimeOffset instant) => !now.IsBefore(instant);
    public static bool operator >(SignalClock now, DateTimeOffset instant) => now.IsAfter(instant);
    public static bool operator <=(SignalClock now, DateTimeOffset instant) => !now.IsAfter(instant);

    public static bool operator >(DateTimeOffset instant, SignalClock now) => now < instant;
    public static bool operator <=(DateTimeOffset instant, SignalClock now) => now >= instant;
    public static bool operator <(DateTimeOffset instant, SignalClock now) => now > instant;
    public static bool operator >=(DateTimeOffset instant, SignalClock now) => now <= instant;

    public static SignalElapsed operator -(SignalClock now, DateTimeOffset since) => new(now, since);
}

public readonly struct SignalElapsed
{
    readonly SignalClock _now;
    readonly DateTimeOffset _since;

    internal SignalElapsed(SignalClock now, DateTimeOffset since)
    {
        _now = now;
        _since = since;
    }

    DateTimeOffset At(TimeSpan duration)
    {
        if (duration >= TimeSpan.Zero)
            return DateTimeOffset.MaxValue - _since <= duration ? DateTimeOffset.MaxValue : _since + duration;

        return duration <= DateTimeOffset.MinValue - _since ? DateTimeOffset.MinValue : _since + duration;
    }

    public static bool operator >=(SignalElapsed elapsed, TimeSpan duration) => elapsed._now >= elapsed.At(duration);
    public static bool operator <(SignalElapsed elapsed, TimeSpan duration) => elapsed._now < elapsed.At(duration);
    public static bool operator >(SignalElapsed elapsed, TimeSpan duration) => elapsed._now > elapsed.At(duration);
    public static bool operator <=(SignalElapsed elapsed, TimeSpan duration) => elapsed._now <= elapsed.At(duration);

    public static bool operator <=(TimeSpan duration, SignalElapsed elapsed) => elapsed >= duration;
    public static bool operator >(TimeSpan duration, SignalElapsed elapsed) => elapsed < duration;
    public static bool operator <(TimeSpan duration, SignalElapsed elapsed) => elapsed > duration;
    public static bool operator >=(TimeSpan duration, SignalElapsed elapsed) => elapsed <= duration;
}
