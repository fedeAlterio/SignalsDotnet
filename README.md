# Angular Signals for .Net
This library is a porting of the Angular Signals in the .Net World, adapted to the .Net MVVM UI Frameworks and based on ReactiveX.
If you need an introduction to what a signal is, try to see: https://angular.io/guide/signals.

# Get Started
It is really easy to get started. What you need to do is to replace all binded ViewModel Properties and ObservableCollections to Signals:

## Example 1
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
```

## Example 2
```c#
public class YoungestPersonViewModel
{
    public YoungestPersonViewModel()
    {
        YoungestPerson = Signal.Computed(() =>
        {
            var people = from city in Cities.Value.EmptyIfNull()
                         from house in city.Houses.Value.EmptyIfNull()
                         from room in house.Roooms.Value.EmptyIfNull()
                         from person in room.People.Value.EmptyIfNull()
                         select new PersonCoordinates(person, room, house, city);

            var youngestPerson = people.DefaultIfEmpty()
                                       .MinBy(x => x?.Person.Age.Value);
            return youngestPerson;
        });
    }

    public IReadOnlySignal<PersonCoordinates?> YoungestPerson { get; set; }
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
    public CollectionSignal<ObservableCollection<Room>> Roooms { get; } = new();
}

public class City
{
    public CollectionSignal<ObservableCollection<House>> Houses { get; } = new();
}

public record PersonCoordinates(Person Person, Room Room, House House, City City);
```
Every signal implements the IObservable interface, so we cann apply all ReactiveX operators we want to them.
## `Singal<T>`
```c#
    public Signal<Person> Person { get; } = new();
    public Signal<Person> Person2 { get; } = new(config => config with { Comparer = new CustomPersonEqualityComparer() });
```

A `Singal<T>` is a wrapper around a `T`. It has a property `Value` that can be set, and that when changed raises the INotifyPropertyChanged event.


It is possible to specify a custom `EqualityComparer` that will be used to check if raise the `PropertyChanged` event. It is also possible to force it to raise the event everytime someone sets the property

## `CollectionSingal<TObservableCollection>`

A `Singal<TObservableCollection>` is a wrapper around an `ObservableCollection` (or in general something that implements the `INotifyCollectionChanged` interface). It listens to both changes of its Value Property, and modifications of the `ObservableCollection` it is wrapping


It is possible to specify a custom `EqualityComparer` that will be used to check if raise the `PropertyChanged` event. It is also possible to force it to raise the event everytime someone sets the property


By default, it subscribes to the `INotifyCollection` event weakly in order to avoid memory leaks, but this behavior can be customized. 


It is also possible to Apply some Throttle-like behavior on the collection changes or more in generale map the IObservable used.
```c#
// This signal notify changes whenever the collection is modified
// ThrottleOneCycle is used to throttle notifications for one rendering cycle,
// In that way we ensure that for example AddRange() calls over the observableCollection Will produce only 1 notification
public CollectionSignal<ObservableCollection<Person>> People { get; } = new(collectionChangedConfiguration: config => config.ThrottleOneCycle(UIReactiveScheduler))
```

## Computed Signals
```c#
public class LoginViewModel
{
  public LoginViewModel()
  {
      CanLogin = Signal.Computed(() => !string.IsNullOrWhiteSpace(Username.Value) && !string.IsNullOrWhiteSpace(Password.Value));
  }

  public Signal<string> Username { get; } = new();
  public Signal<string> Password { get; } = new();
  public IReadOnlySignal<bool> CanLogin { get; }
}
```
A computed signal, is a signal that depends by other signals. 

Basically to create it you need to pass a function that computes the value.

It automatically recognize which are the signals it depends by, and listen for them to change. Whenever a signal changes, the function is executed again, and a new value is produced (the `INotifyPropertyChanged` is raised).

It is possible to specify whether or not to subscribe weakly (default option), or strongly. It is possible also here to specify a custom `EqualityComparer`
### How it works?

Basically the getter (not the setter!) of the Signals property Value raises a static event that notifies someone just requested that signal. 

This is used by the Computed signal before executing the computation function.

The computed signals register to that event (filtering out notifications of other threads), and in that way they know, when the function returns, what are the signals that have been just accessed.

At this point it subscribes to the changes of all those signals in order to know when it should recompute again the value. 

When any signal changes, it repeats the same reasoning and tracks what signals are accessed before recomputing the next value (etc.)

## Untracked

To shutdown the automatical tracking of signals changes in computed signals it is possible to use `Signal.Untracked` or the equivalent properties shortcuts
```c#
public class LoginViewModel
{
   public LoginViewModel()
   {
       CanLogin = Signal.Computed(() =>
       {
           return !string.IsNullOrWhiteSpace(Username.Value) && Signal.Untracked(() => !string.IsNullOrWhiteSpace(Password.Value));
       });
       
       CanLogin = Signal.Computed(() => !string.IsNullOrWhiteSpace(Username.Value) && !string.IsNullOrWhiteSpace(Password.UntrackedValue));

       var AnyPeople = Signal.Computed(() => People.UntrackedValue);
       var AnyPeople2 = Signal.Computed(() => People.UntrackedCollectionChangedValue);
   }

   public CollectionSignal<ObservableCollection<Person>> People { get; } = new();
   public Signal<string> Username { get; } = new();
   public Signal<string> Password { get; } = new();
   public IReadOnlySignal<bool> CanLogin { get; }
}

```

