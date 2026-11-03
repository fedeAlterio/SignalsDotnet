using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;

namespace SignalsDotnet;

public static partial class ComputedSignalFactoryEx
{
    public static IReadOnlySignal<ChangeStamp<T>> Stamped<T>(this IComputedSignalFactory @this,
                                                             Func<T> expression,
                                                             SignalClock clock = default,
                                                             ReadonlySignalConfigurationDelegate<ChangeStamp<T>>? configuration = null)
    {
        var createdAt = clock.TimeProvider.GetUtcNow();
        Optional<ChangeStamp<T>> last = default;
        return @this.Computed(() =>
        {
            var value = expression();
            if (last.TryGetValue(out var previous) && EqualityComparer<T>.Default.Equals(previous.Value, value))
                return previous;

            var stamp = new ChangeStamp<T>(value, last.HasValue ? clock.TimeProvider.GetUtcNow() : createdAt);
            last = new(stamp);
            return stamp;
        }, configuration);
    }

    public static IReadOnlySignal<DateTimeOffset> ChangedAt<T>(this IComputedSignalFactory @this, Func<T> expression, SignalClock clock = default)
    {
        var stamped = @this.Stamped(expression, clock);
        return @this.Computed(() => stamped.Value.ChangedAt);
    }

    public static IReadOnlySignal<bool> HeldFor(this IComputedSignalFactory @this, Func<bool> condition, TimeSpan duration, SignalClock clock = default)
    {
        var stamped = @this.Stamped(condition, clock);
        return @this.Computed(() =>
        {
            var stamp = stamped.Value;
            return stamp.Value && clock - stamp.ChangedAt >= duration;
        });
    }

    public static IReadOnlySignal<bool> TrueWithin(this IComputedSignalFactory @this, Func<bool> condition, TimeSpan duration, SignalClock clock = default)
    {
        var stamped = @this.Stamped(condition, clock);
        var wasTrue = false;
        return @this.Computed(() =>
        {
            var stamp = stamped.Value;
            if (stamp.Value)
                return wasTrue = true;

            return wasTrue && clock - stamp.ChangedAt < duration;
        });
    }

    public static IReadOnlySignal<bool> StableFor<T>(this IComputedSignalFactory @this, Func<T> expression, TimeSpan duration, SignalClock clock = default)
    {
        var stamped = @this.Stamped(expression, clock);
        return @this.Computed(() => clock - stamped.Value.ChangedAt >= duration);
    }

    public static IReadOnlySignal<DateTimeOffset?> TrueSince(this IComputedSignalFactory @this, Func<bool> condition, SignalClock clock = default)
    {
        var stamped = @this.Stamped(condition, clock);
        return @this.Computed(() =>
        {
            var stamp = stamped.Value;
            return stamp.Value ? stamp.ChangedAt : (DateTimeOffset?)null;
        });
    }

    public static IReadOnlySignal<DateTimeOffset?> Rising(this IComputedSignalFactory @this, Func<bool> condition, SignalClock clock = default)
    {
        var stamped = @this.Stamped(condition, clock);
        Optional<ChangeStamp<bool>> previous = default;
        DateTimeOffset? edge = null;
        return @this.Computed(() =>
        {
            var stamp = stamped.Value;
            if (stamp.Value && previous.TryGetValue(out var last) && last != stamp)
                edge = stamp.ChangedAt;

            previous = new(stamp);
            return edge;
        });
    }

    public static IReadOnlySignal<DateTimeOffset?> Falling(this IComputedSignalFactory @this, Func<bool> condition, SignalClock clock = default)
        => @this.Rising(() => !condition(), clock);
}
