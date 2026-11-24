using FluentAssertions;

namespace SignalsDotnet.Tests;

public class EffectConcurrencyTests
{
    static readonly TimeSpan WaitForTheOtherEffect = TimeSpan.FromSeconds(2);
    static readonly TimeSpan HoldTheFirstEffectAtMost = TimeSpan.FromSeconds(15);

    [Fact(Timeout = 20_000)]
    public async Task EffectsOnDifferentThreadsShouldNotWaitForEachOther()
    {
        var first = new Signal<int>();
        var second = new Signal<int>();
        using var firstEffectEntered = new ManualResetEventSlim();
        using var releaseFirstEffect = new ManualResetEventSlim();
        using var secondEffectRan = new ManualResetEventSlim();

        using var firstEffect = new Effect(() =>
        {
            if (first.Value == 0)
            {
                return;
            }

            firstEffectEntered.Set();
            releaseFirstEffect.Wait(HoldTheFirstEffectAtMost);
        });

        using var secondEffect = new Effect(() =>
        {
            if (second.Value != 0)
            {
                secondEffectRan.Set();
            }
        });

        var blockedFirstEffect = Task.Factory.StartNew(() => first.Value = 1, TaskCreationOptions.LongRunning);
        try
        {
            firstEffectEntered.Wait(WaitForTheOtherEffect).Should().BeTrue();

            var secondChange = Task.Factory.StartNew(() => second.Value = 1, TaskCreationOptions.LongRunning);
            secondEffectRan.Wait(WaitForTheOtherEffect).Should().BeTrue();
            await secondChange;
        }
        finally
        {
            releaseFirstEffect.Set();
            await blockedFirstEffect;
        }
    }
}
