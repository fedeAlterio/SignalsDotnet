using FluentAssertions;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;
public class AsyncEffectTests
{
    [Fact]
    public async Task ShouldRunWhenAnySignalChanges()
    {
        await this.SwitchToMainThread();

        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        var effect = new Effect(async _ =>
        {
            await Task.Yield();
            sum = number1.Value + number2.Value;
            await Task.Yield();
        });

        await TestHelpers.WaitUntil(() => sum == 0);

        number1.Value = 1;
        await TestHelpers.WaitUntil(() => sum == 1);

        number1.Value = 2;
        await TestHelpers.WaitUntil(() => sum == 2);

        number2.Value = 2;
        await TestHelpers.WaitUntil(() => sum == 4);

        effect.Dispose();

        number2.Value = 3;
        await TestHelpers.WaitUntil(() => sum == 4);
    }


    [Fact]
    public async Task ShouldRunOnSpecifiedScheduler()
    {
        await this.SwitchToMainThread();
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        var effect = new Effect(async _ =>
        {
            await Task.Yield();
            sum = number1.Value + number2.Value;
            await Task.Yield();
        }, scheduler: scheduler);

        await TestHelpers.WaitUntil(() => sum == 0);

        number1.Value = 1;
        await TestHelpers.WaitUntil(() => sum == 0);

        number1.Value = 2;
        await TestHelpers.WaitUntil(() => sum == 0);

        scheduler.ExecuteAllPendingActions();
        await TestHelpers.WaitUntil(() => sum == 2);

        effect.Dispose();

        number2.Value = 3;
        scheduler.ExecuteAllPendingActions();
        await TestHelpers.WaitUntil(() => sum == 2);
    }

    
    [Fact]
    public async Task EffectsShouldRunAtTheEndOfAtomicOperations()
    {
        await this.SwitchToMainThread();

        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        _ = new Effect(async _ =>
        {
            await Task.Yield();
            sum = number1.Value + number2.Value;
            await Task.Yield();
        });
        await TestHelpers.WaitUntil(() => sum == 0);

        await Effect.AtomicOperationAsync(async () =>
        {
            await Task.Yield();
            number1.Value = 1;
            await Task.Yield();
            sum.Should().Be(0);

            await Task.Yield();
            number1.Value = 2;
            await Task.Yield();
            sum.Should().Be(0);
        });
        await TestHelpers.WaitUntil(() => sum == 2);

        await Effect.AtomicOperationAsync(async () =>
        {
            await Task.Yield();
            number2.Value = 2;
            sum.Should().Be(2);

            await Effect.AtomicOperationAsync(async () =>
            {
                await Task.Yield();
                number2.Value = 3;
                await Task.Yield();
                sum.Should().Be(2);
                await Task.Yield();
            });

            sum.Should().Be(2);
        });

        await TestHelpers.WaitUntil(() => sum == 5);
    }
}
