<img src="./assets/demo.gif"/>

# SignalsDotnet

[![NuGet](https://img.shields.io/nuget/v/SignalsDotnet.svg)](https://www.nuget.org/packages/SignalsDotnet)
[![License](https://img.shields.io/github/license/fedeAlterio/SignalsDotnet)](LICENSE)

## Angular Signals for .NET
This library is a port of Angular Signals to the .NET world, adapted for .NET MVVM UI frameworks and built on top of [R3](https://github.com/Cysharp/R3) (a variant of ReactiveX).

If you need an introduction to what signals are, see: https://angular.io/guide/signals

**Current Version:** 2.0.5  
**Target Framework:** .NET 8.0

## Table of Contents

- [Get Started](#get-started)
- [Core Concepts](#core-concepts)
- [Basic Examples](#basic-examples)
- [Signal Types](#signal-types)
  - [Signal&lt;T&gt;](#signalt)
  - [CollectionSignal&lt;T&gt;](#collectionsignalt)
  - [Factory Methods](#factory-methods)
- [Computed Signals & Linked Signals](#computed-signals--linked-signals)
  - [Creating Computed Signals](#creating-computed-signals)
  - [Linked Signals](#linked-signals)
  - [Async Computed Signals](#async-computed-signals)
  - [Using ComputedSignalFactory](#using-computedsignalfactory)
  - [ConcurrentChangeStrategy](#concurrentchangestrategy)
  - [How it Works](#how-it-works)
- [Effects](#effects)
  - [Synchronous Effects](#synchronous-effects)
  - [Asynchronous Effects](#asynchronous-effects)
  - [Atomic Operations](#atomic-operations)
  - [Custom Schedulers](#custom-schedulers)
- [Advanced Features](#advanced-features)
  - [Untracked](#untracked)
  - [Signal Events](#signal-events)
  - [WhenAnyChanged](#whenanychanged)
  - [CancellationSignal](#cancellationsignal)

---

# Get Started

It is really easy to get started. Replace all bound ViewModel properties and ObservableCollections with Signals to get automatic change tracking and reactive updates.

## Core Concepts

### Signal Types

- **`Signal<T>`** - A writable signal that holds a value of type T
- **`IReadOnlySignal<T>`** - A read-only signal (computed or readonly)
- **`IAsyncReadOnlySignal<T>`** - A read-only signal with async computation
- **`ISignal<T>`** - A writable signal interface (linked signals)
- **`IAsyncSignal<T>`** - A writable signal with async computation
- **`CollectionSignal<T>`** - A signal wrapping an ObservableCollection

### Key Features

✅ **Multi-Platform** - Works with MAUI, WPF, Avalonia, Uno Platform, Blazor, Unity, Godot, and other .NET frameworks  
✅ **Automatic Dependency Tracking** - Signals automatically track their dependencies  
✅ **Computed Signals** - Derive values from other signals automatically  
✅ **Async Support** - Full support for asynchronous computations with cancellation  
✅ **Collection Signals** - Specialized signals that support ObservableCollections 
✅ **Effects** - Run side effects when signals change  
✅ **Signal Events** - Events that will cause computed signals to recompute  
✅ **Full Rx Power** - Signals are Observables, giving you access to the entire R3/ReactiveX ecosystem  
✅ **Memory Efficient** - Support Weak subscriptions to prevent memory leaks

---

## Basic Examples

### Example 1: Simple Login Form
```c#
public class LoginViewModel
{
    public LoginViewModel()
    {
        CanLogin = Signal.Computed(() => !string.IsNullOrWhiteSpace(Username.Value) && !string.IsNullOrWhiteSpace(Password.Value));
        LoginCommand = new DelegateCommand(Login, () => CanLogin.Value).RaiseCanExecuteChangedAutomatically();
    }

    public Signal<string> Username { get; } = new();
    public Signal<string> Password { get; } = new();
    public IReadOnlySignal<bool> CanLogin { get; }

    public ICommand LoginCommand { get; }
    public void Login() { /* Login */ }
}

public static class DelegateCommandExtensions
{
    // This is specific for Prism, but the same approach can be used in other MVVM Frameworks
    public static T RaiseCanExecuteChangedAutomatically<T>(this T @this) where T : DelegateCommand
    {
        var signal = Signal.Computed(@this.CanExecute, config => config with { SubscribeWeakly = false });
        signal.Subscribe(_ => @this.RaiseCanExecuteChanged());
        _ = signal.Value;
        return @this;
    }
}
```

### Example 2: Async Validation with Computed Factory
```c#
public class LoginViewModel
{
    // Value set from outside.
    public Signal<bool> IsDeactivated { get; } = new(false);
    
    public LoginViewModel()
    {      
        var computedFactory = ComputedSignalFactory.Default
            .DisconnectEverythingWhen(IsDeactivated.Values)
            .OnException(exception =>
            {
                /* log or do something with it */
            });

        // Will be cancelled on deactivation, or if the username signal changes during the await
        IsUsernameValid = computedFactory.AsyncComputed(
            async cancellationToken => await IsUsernameValidAsync(Username.Value, cancellationToken),
            false, 
            ConcurrentChangeStrategy.CancelCurrent);

        // Async computed signals have a (sync) signal that notifies us when the async computation is running
        CanLogin = computedFactory.Computed(() => !IsUsernameValid.IsComputing.Value
                                                  && IsUsernameValid.Value
                                                  && !string.IsNullOrWhiteSpace(Password.Value));

        computedFactory.Effect(UpdateApiCalls);

        // This signal will be recomputed both when the collection changes, and when endDate of the last element changes automatically!
        TotalApiCallsText = computedFactory.Computed(() =>
        {
            var lastCall = ApiCalls.Value.LastOrDefault();
            return $"Total api calls: {ApiCalls.Value.Count}. Last started at {lastCall?.StartedAt}, and ended at {lastCall?.EndedAt.Value}";
        })!;
    }

    public Signal<string?> Username { get; } = new("");
    public Signal<string> Password { get; } = new("");
    public IAsyncReadOnlySignal<bool> IsUsernameValid { get; }
    public IReadOnlySignal<bool> CanLogin { get; }
    public IReadOnlySignal<string> TotalApiCallsText { get; }
    public IReadOnlySignal<ObservableCollection<ApiCall>> ApiCalls { get; } = new ObservableCollection<ApiCall>().ToCollectionSignal();

    async Task<bool> IsUsernameValidAsync(string? username, CancellationToken cancellationToken)
    {
        await Task.Delay(3000, cancellationToken);
        return username?.Length > 2;
    }
    
    void UpdateApiCalls()
    {
        var isComputingUsername = IsUsernameValid.IsComputing.Value;
        using var _ = Signal.UntrackedScope();

        if (isComputingUsername)
        {
            ApiCalls.Value.Add(new ApiCall(startedAt: DateTime.Now));
            return;
        }

        var call = ApiCalls.Value.LastOrDefault();
        if (call is { EndedAt.Value: null })
        {
            call.EndedAt.Value = DateTime.Now;
        }
    }
}

public class ApiCall(DateTime startedAt)
{
    public DateTime StartedAt => startedAt;
    public Signal<DateTime?> EndedAt { get; } = new();
}
```

### Example 3: Deep Reactive Collections
```c#
public class YoungestPersonViewModel
{
    public YoungestPersonViewModel()
    {
        YoungestPerson = Signal.Computed(() =>
        {
            var people = from city in Cities.Value.EmptyIfNull()
                         from house in city.Houses.Value.EmptyIfNull()
                         from room in house.Rooms.Value.EmptyIfNull()
                         from person in room.People.Value.EmptyIfNull()
                         select new PersonCoordinates(person, room, house, city);

            var youngestPerson = people.DefaultIfEmpty()
                                       .MinBy(x => x?.Person.Age.Value);
            return youngestPerson;
        });
    }

    public IReadOnlySignal<PersonCoordinates?> YoungestPerson { get; }
    public CollectionSignal<ObservableCollection<City>> Cities { get; } = new();
}

public class Person
{
    public Signal<int> Age { get; } = new();
}

public class Room
{
    public CollectionSignal<ObservableCollection<Person>> People { get; } = new();
}

public class House
{
    public CollectionSignal<ObservableCollection<Room>> Rooms { get; } = new();
}

public class City
{
    public CollectionSignal<ObservableCollection<House>> Houses { get; } = new();
}

public record PersonCoordinates(Person Person, Room Room, House House, City City);
```

---

## Signal Types

Every signal has a `Values` property that is an `Observable<T>` and notifies whenever the signal changes. Signals also provide `FutureValues` which skips the current value and only notifies on future changes.

### `Signal<T>`

A `Signal<T>` is a writable signal wrapper around a value of type `T`. It implements `INotifyPropertyChanged` and raises the `PropertyChanged` event when its value changes.

```c#
// Basic signal
public Signal<Person> Person { get; } = new();

// Signal with custom equality comparer
public Signal<Person> Person2 { get; } = new(config => config with 
{ 
    Comparer = new CustomPersonEqualityComparer() 
});

// Signal with initial value
public Signal<string> Username { get; } = new("initial value");

// Signal that always raises PropertyChanged (even for same values)
public Signal<int> Counter { get; } = new(config => config with 
{ 
    RaiseOnlyWhenChanged = false 
});
```

**Configuration Options:**
- `Comparer` - Custom `IEqualityComparer<T>` to determine when to raise PropertyChanged
- `RaiseOnlyWhenChanged` - Whether to raise PropertyChanged only when value actually changes (default: true)

### `CollectionSignal<TObservableCollection>`

A `CollectionSignal<TObservableCollection>` wraps an `ObservableCollection` (or any `INotifyCollectionChanged`) and listens to both:
1. Changes to its `Value` property
2. Modifications within the collection itself (Add, Remove, Clear, etc.)

This enables deep reactive tracking - computed signals automatically update when items are added/removed or when nested properties change.

```c#
// Basic collection signal
public CollectionSignal<ObservableCollection<Person>> People { get; } = new();

// Collection signal with throttling to batch notifications
public CollectionSignal<ObservableCollection<Person>> People { get; } = new(
    collectionChangedConfiguration: config => config.ThrottleOneCycle(UIReactiveScheduler)
);
```

**Why use throttling?** Operations like `AddRange()` trigger multiple `CollectionChanged` events. Throttling batches these into a single notification per UI frame, improving performance.

**Configuration Options:**
- `collectionChangedConfiguration` - Configure how collection change events are processed (throttling, filtering, etc.)
- `propertyChangedConfiguration` - Configure the signal's property changed behavior
- `SubscribeWeakly` - Whether to subscribe to collection events weakly (default: false) to prevent memory leaks

### Factory Methods

```c#
// Create signals using factory methods
var signal = Signal.Create<string>();
var signalWithValue = Signal.Create("initial");

// Convert Observable to Signal
Observable<int> observable = /* ... */;
IReadOnlySignal<int> signal = observable.ToSignal();
ISignal<int> linkedSignal = observable.ToLinkedSignal();

// Create collection signal from existing collection
ObservableCollection<Person> collection = new();
IReadOnlySignal<ObservableCollection<Person>> signal = collection.ToCollectionSignal();

// Create from observable with configuration
var signal = Observable.Interval(TimeSpan.FromSeconds(1))
                       .ToSignal(config => config with { RaiseOnlyWhenChanged = false });
```

---

## Computed Signals & Linked Signals

Computed signals automatically derive their values from other signals. They track dependencies automatically and recompute when any dependency changes.

### Creating Computed Signals

```c#
var firstName = new Signal<string>("John");
var lastName = new Signal<string>("Doe");

// Automatically updates when firstName or lastName changes
var fullName = Signal.Computed(() => $"{firstName.Value} {lastName.Value}");

Console.WriteLine(fullName.Value); // "John Doe"
firstName.Value = "Jane";
Console.WriteLine(fullName.Value); // "Jane Doe"
```

### Linked Signals

Linked signals are computed signals that can also be manually written to:

```c#
var source = new Signal<int>(10);
var linked = Signal.Linked(() => source.Value * 2);

Console.WriteLine(linked.Value); // 20

// Can be manually overridden
linked.Value = 100;
Console.WriteLine(linked.Value); // 100

// Automatically recomputes when source changes
source.Value = 5;
Console.WriteLine(linked.Value); // 10
```

### Async Computed Signals

```c#
var username = new Signal<string>();

var isUsernameValid = Signal.AsyncComputed(
    async cancellationToken => 
    {
        var user = username.Value;
        return await ValidateUsernameAsync(user, cancellationToken);
    },
    defaultValue: false,
    ConcurrentChangeStrategy.CancelCurrent
);

// Check if computation is running
if (isUsernameValid.IsComputing.Value)
{
    Console.WriteLine("Validating...");
}
```

### Using ComputedSignalFactory

For more control over computed signals, use `ComputedSignalFactory`:

```c#
public class LoginViewModel
{
    public Signal<bool> IsDeactivated { get; } = new(false);

    public LoginViewModel()
    {      
        var computedFactory = ComputedSignalFactory.Default
            .DisconnectEverythingWhen(IsDeactivated.Values)
            .OnException(exception =>
            {
                Logger.LogError(exception, "Computation error");
            });

        // All signals created from this factory will be cancelled when IsDeactivated is true
        IsUsernameValid = computedFactory.AsyncComputed(
            async cancellationToken => await IsUsernameValidAsync(Username.Value, cancellationToken),
            false, 
            ConcurrentChangeStrategy.CancelCurrent
        );

        CanLogin = computedFactory.Computed(() => 
            !IsUsernameValid.IsComputing.Value &&
            IsUsernameValid.Value &&
            !string.IsNullOrWhiteSpace(Password.Value)
        );

        // Effects are also created from the factory
        computedFactory.Effect(UpdateApiCalls);
    }

    public Signal<string?> Username { get; } = new();
    public Signal<string> Password { get; } = new();
    public IAsyncReadOnlySignal<bool> IsUsernameValid { get; }
    public IReadOnlySignal<bool> CanLogin { get; }

    async Task<bool> IsUsernameValidAsync(string? username, CancellationToken cancellationToken)
    {
        await Task.Delay(3000, cancellationToken);
        return username?.Length > 2;
    }

    void UpdateApiCalls()
    {
        // Effect logic here
    }
}
```

### ConcurrentChangeStrategy

In an async computed signal, dependencies can change while the computation function is running. Use `ConcurrentChangeStrategy` to control this behavior:

- **`ConcurrentChangeStrategy.CancelCurrent`** - Cancels the current computation and starts a new one immediately
- **`ConcurrentChangeStrategy.ScheduleNext`** - Queues the next computation to run after the current one completes (max 1 queued)

Both strategies respect the `DisconnectEverythingWhen` cancellation.

### How it Works

Computed signals use automatic dependency tracking:

1. Before executing the computation function, the signal subscribes to a tracking event
2. When any signal's `Value` getter is called, it notifies the tracker
3. The computed signal subscribes to all accessed signals
4. When any dependency changes, the computation reruns and tracks dependencies again

This dynamic tracking means computed signals only subscribe to signals that are actually accessed in each execution.

---

## Effects

Effects are reactive side effects that automatically track signal dependencies and re-run when any dependency changes. They are similar to computed signals but are used for side effects instead of computing values.

### Synchronous Effects
```c#
public class ViewModel
{
    public Signal<int> Counter { get; } = new();
    
    public ViewModel()
    {
        // Effect runs immediately and re-runs whenever Counter changes
        var effect = new Effect(() => 
        {
            Console.WriteLine($"Counter value: {Counter.Value}");
        });
    }
}
```

### Asynchronous Effects
```c#
public class ViewModel
{
    public Signal<string> SearchTerm { get; } = new();
    
    public ViewModel()
    {
        var effect = new Effect(async cancellationToken =>
        {
            var term = SearchTerm.Value;
            await SearchAsync(term, cancellationToken);
        }, ConcurrentChangeStrategy.CancelCurrent);
    }
}
```

### Atomic Operations

Effects can be batched using atomic operations to prevent multiple executions during complex updates:

```c#
Effect.AtomicOperation(() =>
{
    signal1.Value = 1;
    signal2.Value = 2;
    signal3.Value = 3;
    // Effect runs only once after all changes
});

// Async version
await Effect.AtomicOperationAsync(async () =>
{
    await Task.Yield();
    signal1.Value = 1;
    await Task.Yield();
    signal2.Value = 2;
    // Effect runs only once after all changes
});
```

### Custom Schedulers

You can specify a custom scheduler for effect execution:

```c#
var scheduler = TimeProvider.System;
var effect = new Effect(() => 
{
    // This will be scheduled on the specified scheduler
    DoSomething();
}, scheduler);
```

---

## Advanced Features

### Untracked

To disable automatic tracking of signal changes in computed signals, use `Signal.Untracked()` or the equivalent property shortcuts:

```c#
public class LoginViewModel
{
   public LoginViewModel()
   {
       // Using Untracked() method
       CanLogin = Signal.Computed(() =>
       {
           return !string.IsNullOrWhiteSpace(Username.Value) && 
                  Signal.Untracked(() => !string.IsNullOrWhiteSpace(Password.Value));
       });
       
       // Using UntrackedValue property
       CanLogin = Signal.Computed(() => !string.IsNullOrWhiteSpace(Username.Value) && 
                                       !string.IsNullOrWhiteSpace(Password.UntrackedValue));

       // For collection signals
       var anyPeople = Signal.Computed(() => People.UntrackedValue);
       var anyPeople2 = Signal.Computed(() => People.UntrackedCollectionChangedValue);
   }

   public CollectionSignal<ObservableCollection<Person>> People { get; } = new();
   public Signal<string> Username { get; } = new();
   public Signal<string> Password { get; } = new();
   public IReadOnlySignal<bool> CanLogin { get; }
}
```

### Signal Events

Signal Events are signals that always notify subscribers, even when set to the same value. They're useful for event-driven scenarios:

```c#
public class ViewModel
{
    public ISignal<Unit> RefreshRequested { get; } = Signal.CreateEvent();
    
    public void RequestRefresh()
    {
        RefreshRequested.Invoke(); // Always triggers notification
    }
    
    public ViewModel()
    {
        var effect = new Effect(() =>
        {
            RefreshRequested.Track(); // Track the event
            // This runs every time Invoke() is called
            PerformRefresh();
        });
    }
}
```

### WhenAnyChanged

Combine multiple signals into a single observable that emits whenever any of them changes:

```c#
var signal1 = new Signal<int>();
var signal2 = new Signal<string>();
var signal3 = new Signal<bool>();

Observable<Unit> anyChanged = Signal.WhenAnyChanged(signal1, signal2, signal3);
anyChanged.Subscribe(_ => Console.WriteLine("At least one signal changed"));
```

### CancellationSignal

Convert a boolean observable into a signal that provides cancellation tokens:

```c#
Observable<bool> isDeactivated = this.IsDeactivated();
IReadOnlySignal<CancellationToken> cancellationSignal = CancellationSignal.Create(isDeactivated);

// Use the cancellation token in async operations
await SomeAsyncOperation(cancellationSignal.Value);
```

---

## License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## Repository

GitHub: https://github.com/fedeAlterio/SignalsDotnet
