using SignalsDotnet.Configuration;

namespace SignalsDotnet;

public static partial class Signal
{
    public static SignalClock Now => SignalClock.System;

    public static SignalClock ToSignalClock(this TimeProvider timeProvider) => new(timeProvider);

    public static IReadOnlySignal<ChangeStamp<T>> Stamped<T>(Func<T> expression, SignalClock clock = default, ReadonlySignalConfigurationDelegate<ChangeStamp<T>>? configuration = null)
        => ComputedSignalFactory.Default.Stamped(expression, clock, configuration);

    public static IReadOnlySignal<ChangeStamp<T>> Stamped<T>(this IReadOnlySignal<T> source, SignalClock clock = default, ReadonlySignalConfigurationDelegate<ChangeStamp<T>>? configuration = null)
        => ComputedSignalFactory.Default.Stamped(() => source.Value, clock, configuration);

    public static IReadOnlySignal<DateTimeOffset> ChangedAt<T>(Func<T> expression, SignalClock clock = default)
        => ComputedSignalFactory.Default.ChangedAt(expression, clock);

    public static IReadOnlySignal<DateTimeOffset> ChangedAt<T>(this IReadOnlySignal<T> source, SignalClock clock = default)
        => ComputedSignalFactory.Default.ChangedAt(() => source.Value, clock);

    public static IReadOnlySignal<bool> HeldFor(Func<bool> condition, TimeSpan duration, SignalClock clock = default)
        => ComputedSignalFactory.Default.HeldFor(condition, duration, clock);

    public static IReadOnlySignal<bool> HeldFor(this IReadOnlySignal<bool> condition, TimeSpan duration, SignalClock clock = default)
        => ComputedSignalFactory.Default.HeldFor(() => condition.Value, duration, clock);

    public static IReadOnlySignal<bool> TrueWithin(Func<bool> condition, TimeSpan duration, SignalClock clock = default)
        => ComputedSignalFactory.Default.TrueWithin(condition, duration, clock);

    public static IReadOnlySignal<bool> TrueWithin(this IReadOnlySignal<bool> condition, TimeSpan duration, SignalClock clock = default)
        => ComputedSignalFactory.Default.TrueWithin(() => condition.Value, duration, clock);

    public static IReadOnlySignal<bool> StableFor<T>(Func<T> expression, TimeSpan duration, SignalClock clock = default)
        => ComputedSignalFactory.Default.StableFor(expression, duration, clock);

    public static IReadOnlySignal<bool> StableFor<T>(this IReadOnlySignal<T> source, TimeSpan duration, SignalClock clock = default)
        => ComputedSignalFactory.Default.StableFor(() => source.Value, duration, clock);

    public static IReadOnlySignal<DateTimeOffset?> TrueSince(Func<bool> condition, SignalClock clock = default)
        => ComputedSignalFactory.Default.TrueSince(condition, clock);

    public static IReadOnlySignal<DateTimeOffset?> TrueSince(this IReadOnlySignal<bool> condition, SignalClock clock = default)
        => ComputedSignalFactory.Default.TrueSince(() => condition.Value, clock);

    public static IReadOnlySignal<DateTimeOffset?> Rising(Func<bool> condition, SignalClock clock = default)
        => ComputedSignalFactory.Default.Rising(condition, clock);

    public static IReadOnlySignal<DateTimeOffset?> Rising(this IReadOnlySignal<bool> condition, SignalClock clock = default)
        => ComputedSignalFactory.Default.Rising(() => condition.Value, clock);

    public static IReadOnlySignal<DateTimeOffset?> Falling(Func<bool> condition, SignalClock clock = default)
        => ComputedSignalFactory.Default.Falling(condition, clock);

    public static IReadOnlySignal<DateTimeOffset?> Falling(this IReadOnlySignal<bool> condition, SignalClock clock = default)
        => ComputedSignalFactory.Default.Falling(() => condition.Value, clock);
}
