using FluentAssertions;
using R3;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class EffectTests
{
    [Fact]
    public async Task ShouldRunWhenAnySignalChanges()
    {
        await this.SwitchToMainThread();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        var effect = new Effect(() => sum = number1.Value + number2.Value);
        sum.Should().Be(0);

        number1.Value = 1;
        sum.Should().Be(1);

        number1.Value = 2;
        sum.Should().Be(2);

        number2.Value = 2;
        sum.Should().Be(4);

        effect.Dispose();

        number2.Value = 3;
        sum.Should().Be(4);
    }

    [Fact]
    public async Task ShouldRunOnSpecifiedScheduler()
    {
        await this.SwitchToMainThread();
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        var effect = new Effect(() => sum = number1.Value + number2.Value, scheduler);
        sum.Should().Be(0);

        number1.Value = 1;
        sum.Should().Be(0);

        number1.Value = 2;
        sum.Should().Be(0);

        scheduler.ExecuteAllPendingActions();
        sum.Should().Be(2);

        effect.Dispose();

        number2.Value = 3;
        scheduler.ExecuteAllPendingActions();
        sum.Should().Be(2);
    }

    [Fact]
    public async Task EffectShouldNotRunMultipleTimesInASingleSchedule()
    {
        await this.SwitchToMainThread();
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int executionsCount = 0;
        var effect = new Effect(() =>
        {
            _ = number1.Value + number2.Value;
            executionsCount++;
        }, scheduler);
        executionsCount.Should().Be(1);

        number2.Value = 4;
        number2.Value = 3;

        number1.Value = 4;
        number1.Value = 3;
        executionsCount.Should().Be(1);

        scheduler.ExecuteAllPendingActions();
        executionsCount.Should().Be(2);

        effect.Dispose();

        number1.Value = 4;
        number1.Value = 3;
        scheduler.ExecuteAllPendingActions();
        executionsCount.Should().Be(2);
    }

    [Fact]
    public async Task EffectsShouldRunAtTheEndOfAtomicOperations()
    {
        await this.SwitchToMainThread();
        await Enumerable.Range(1, 33)
                   .Select(__ =>
                   {
                       return Observable.FromAsync(async token => await Task.Run(() =>
                       {
                           var number1 = new Signal<int>();
                           var number2 = new Signal<int>();

                           int sum = -1;
                           _ = new Effect(() => sum = number1.Value + number2.Value);
                           //sum.Should().Be(0);

                           Effect.AtomicOperation(() =>
                           {
                               number1.Value = 1;
                               sum.Should().Be(0);

                               number1.Value = 2;
                               sum.Should().Be(0);
                           });
                           sum.Should().Be(2);

                           Effect.AtomicOperation(() =>
                           {
                               number2.Value = 2;
                               sum.Should().Be(2);

                               Effect.AtomicOperation(() =>
                               {
                                   number2.Value = 3;
                                   sum.Should().Be(2);
                               });

                               sum.Should().Be(2);
                           });

                           sum.Should().Be(5);
                       }, token));
                   })
                   .Merge()
                   .WaitAsync();
    }

    [Fact]
    public async Task EffectsShouldRunAtTheEndOfAtomicOperationsWithScheduler()
    {
        await this.SwitchToMainThread();
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        _ = new Effect(() => sum = number1.Value + number2.Value, scheduler);
        sum.Should().Be(0);

        Effect.AtomicOperation(() =>
        {
            number1.Value = 1;
            scheduler.ExecuteAllPendingActions();
            sum.Should().Be(0);

            number1.Value = 2;
            scheduler.ExecuteAllPendingActions();
            sum.Should().Be(0);
        });
        sum.Should().Be(0);

        scheduler.ExecuteAllPendingActions();
        sum.Should().Be(2);
    }
}

public class TestScheduler : TimeProvider
{
    Action? _actions;
    public void ExecuteAllPendingActions()
    {
        _actions?.Invoke();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _actions += () => callback(state);
        return new FakeTimer();
    }

    class FakeTimer : ITimer
    {
        public void Dispose()
        {

        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotImplementedException();
    }
}