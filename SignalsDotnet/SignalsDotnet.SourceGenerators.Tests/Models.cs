namespace SignalsDotnet.SourceGenerators.Tests;

[GenerateSignals]
[GenerateNotifyPropertyChanged]
public partial class Person
{
    public partial string Name { get; set; }
    public partial int Age { get; set; }

    [Computed]
    string ComputeFullName() => $"{Name} {Age}";
}

[GenerateSignals]
public partial class WithoutNotification
{
    public partial string Name { get; set; }

    [Computed]
    string ComputeShout() => Name.ToUpperInvariant();
}

[GenerateSignals]
[GenerateNotifyPropertyChanged(false)]
public partial class NotificationDisabled
{
    public partial string Name { get; set; }
}

[GenerateSignals]
[GenerateNotifyPropertyChanged]
public partial class WithInitializationHook
{
    public partial string Name { get; set; }

    [SignalIgnore]
    public string? ShoutAtInitialization { get; private set; }

    [Computed]
    string ComputeShout() => Name.ToUpperInvariant();

    partial void OnInitialized()
    {
        Name = "from hook";
        ShoutAtInitialization = Shout;
    }
}

[GenerateSignals]
public partial class ComputedDeclaredFirst
{
    [Computed]
    string ComputeUpper() => Name.ToUpperInvariant();

    public partial string Name { get; set; }
}

[GenerateSignals]
[GenerateNotifyPropertyChanged]
public partial class ChainedComputed
{
    public partial int Value { get; set; }

    [Computed]
    int ComputeDoubled() => Value * 2;

    [Computed]
    int ComputeQuadrupled() => Doubled * 2;

    [Computed]
    string ComputeDescribed()
    {
        return $"{Value} -> {Quadrupled}";
    }
}

[GenerateSignals]
[GenerateNotifyPropertyChanged]
public partial class AsyncPerson
{
    public partial string Name { get; set; }

    [AsyncComputed]
    async ValueTask<string> ComputeGreeting(CancellationToken token)
    {
        await Task.Yield();
        return $"Hello {Name}";
    }
}

[GenerateSignals]
public partial class AsyncWithTask
{
    public partial int Value { get; set; }

    [AsyncComputed(ConcurrentChangeStrategy = ConcurrentChangeStrategy.CancelCurrent)]
    async Task<int> ComputeDoubled(CancellationToken token)
    {
        await Task.Yield();
        return Value * 2;
    }
}

[GenerateNotifyPropertyChanged]
public partial class PerPropertyOptIn
{
    [Signal]
    public partial string Tracked { get; set; }

    public string Plain { get; set; } = "";
}

public partial class PerPropertyWithComputed
{
    [Signal]
    public partial string Name { get; set; }

    [Computed]
    string ComputeShout() => Name.ToUpperInvariant();
}

[GenerateSignals]
public partial class WithIgnored
{
    public partial string Tracked { get; set; }

    [SignalIgnore]
    public string NotTracked { get; set; } = "";

    public string Computed => Tracked + "!";
}

public partial class Outer
{
    [GenerateSignals]
    public partial class Nested
    {
        public partial string Name { get; set; }
    }
}

[GenerateSignals]
public partial class WithAccessors
{
    public partial string PrivateSetter { get; private set; }
    public partial string GetOnly { get; }
    internal partial int Internal { get; set; }
}

[GenerateSignals]
public partial class Generic<T> where T : class, new()
{
    public partial T? Item { get; set; }
}

public partial class OnlyComputed
{
    [Computed]
    int ComputeConstant() => 42;
}

[GenerateSignals]
[GenerateNotifyPropertyChanged]
public partial record PersonRecord
{
    public partial string Name { get; set; }
    public partial int Age { get; set; }

    [Computed]
    string ComputeFullName() => $"{Name} {Age}";
}

[GenerateSignals]
public partial record struct PointRecordStruct
{
    public partial int X { get; set; }
}

public partial record Container
{
    [GenerateSignals]
    public partial record NestedRecord
    {
        public partial string Name { get; set; }
    }
}
