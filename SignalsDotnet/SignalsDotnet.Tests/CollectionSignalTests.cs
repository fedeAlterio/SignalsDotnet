using System.Collections.ObjectModel;
using FluentAssertions;

namespace SignalsDotnet.Tests;

public class CollectionSignalTests
{
    [Fact]
    public void ReactiveObservableCollection_ShouldObserve_NestedChanges()
    {
        var city = new City();
        bool anyNiceChair = false;
        city.AnyNiceChair.Subscribe(x => anyNiceChair = x);

        var house = new House();
        city.Houses.Value = [house];
        anyNiceChair.Should().BeFalse();

        var room = new Room();
        city.FirstRoom.Value.Should().BeNull();
        house.Rooms.Value.Add(room);
        anyNiceChair.Should().BeFalse();
        city.FirstRoom.Value.Should().Be(room);

        var badChair = new Chair("badChair");
        room.Chairs.Value.Add(badChair);
        anyNiceChair.Should().BeFalse();

        var niceChair = new Chair("NiceChair");
        room.Chairs.Value.Add(niceChair);
        anyNiceChair.Should().BeTrue();

        house.Rooms.Value.Remove(room);
        anyNiceChair.Should().BeFalse();

        house.Rooms.Value.Add(room);
        anyNiceChair.Should().BeTrue();

        city.Houses.Value = null;
        anyNiceChair.Should().BeFalse();
    }
}


public class City
{
    public City()
    {
        AnyNiceChair = Signal.Computed(() =>
        {
            var niceChairs = from house in Houses.Value ?? Enumerable.Empty<House>()
                             from room in house.Rooms.Value
                             from chair in room.Chairs.Value
                             where chair.ChairName == "NiceChair"
                             select chair;

            return niceChairs.Any();
        });

        FirstRoom = Signal.Computed(() => Houses.Value?.SelectMany(x => x.Rooms.Value).FirstOrDefault());
    }


    public CollectionSignal<ObservableCollection<House>> Houses { get; } = new();
    public IReadOnlySignal<bool> AnyNiceChair { get; }
    public IReadOnlySignal<Room?> FirstRoom { get; }
}

public class House
{
    public IReadOnlySignal<ObservableCollection<Room>> Rooms { get; } = new ObservableCollection<Room>().ToCollectionSignal();
}

public class Room
{
    public IReadOnlySignal<ObservableCollection<Chair>> Chairs { get; } = new ObservableCollection<Chair>().ToCollectionSignal();
}

public record Chair(string ChairName);