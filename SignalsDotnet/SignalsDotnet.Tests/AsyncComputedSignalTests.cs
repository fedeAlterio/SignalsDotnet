﻿using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using FluentAssertions;
using SignalsDotnet.Helpers;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;
public class AsyncComputedSignalTests
{
    [Fact]
    public async Task ShouldNotifyWhenAnyChanged()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        async ValueTask<int> Sum(CancellationToken token = default)
        {
            await Task.Yield();
            return prop1.Value + prop2.Value;
        }

        var computed = Signal.AsyncComputed(Sum, 0, () => Optional<int>.Empty);
        int notifiedValue = 0;
        computed.Subscribe(_ => notifiedValue++);
        _ = computed.Value;
        await TestHelpers.WaitUntil(() => notifiedValue == 1);

        notifiedValue = 0;
        prop1.Value = 2;
        await TestHelpers.WaitUntil(() => notifiedValue == 1);
        computed.Value.Should().Be(await Sum());

        notifiedValue = 0;
        prop2.Value = 1;
        await TestHelpers.WaitUntil(() => notifiedValue == 1);
        computed.Value.Should().Be(await Sum());
    }

    [Fact]
    public async Task SignalChangedWhileComputing_ShouldBeConsidered()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var computed = Signal.AsyncComputed(Sum, 0, () => Optional<int>.Empty);


        async ValueTask<int> Sum(CancellationToken token = default)
        {
            await Task.Yield();
            var sum = prop1.Value + prop2.Value;
            if (prop1.Value <= 3)
            {
                prop1.Value++;
                prop2.Value++;
            }
            else
            {
                tcs.SetResult();
            }

            return sum;
        }

        _ = computed.Value;
        await tcs.Task;
        await TestHelpers.WaitUntil(() => computed.Value == prop1.Value + prop2.Value);
    }


    [Fact]
    public async Task ConcurrentUpdate_ShouldCancelCurrentIfRequested()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        CancellationToken computeToken = default;
        var computed = Signal.AsyncComputed(async token =>
        {
            var sum = prop1.Value + prop2.Value;
            if (prop1.Value == 1)
                return sum;

            prop1.Value++;
            computeToken = token;
            await Task.Delay(1, token);
            return sum;
        }, 0, ConcurrentChangeStrategy.CancelCurrent);

        _ = computed.Value;
        await TestHelpers.WaitUntil(() => computeToken.IsCancellationRequested);
    }

    [Fact]
    public async Task OtherSignalChanges_ShouldNotBeConsidered()
    {
        await this.SwitchToMainThread();

        var signal1 = new Signal<int>();
        var signal2 = new Signal<int>();

        var signal3 = new Signal<int>();

        var middleComputationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signal3ChangedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stepNumber = 0;
        var sum = Signal.AsyncComputed(async _ =>
        {
            middleComputationTcs.TrySetResult();
            var ret = stepNumber + signal1.Value + signal2.Value;
            stepNumber++;
            await signal3ChangedTcs.Task;
            return ret;
        }, 0);

        var notifiedCount = 0;
        _ = sum.Value;
        await sum.Where(x => x == 0)
                 .Take(1)
                 .ToTask()
                 .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        sum.Skip(1).Subscribe(_ => notifiedCount++);
        await middleComputationTcs.Task;

        _ = signal3.Value;
        signal3.Value = 1;
        signal3ChangedTcs.SetResult();

        await Task.Yield();
        notifiedCount.Should().Be(0);
        signal1.Value = 1;
        await TestHelpers.WaitUntil(() => sum.Value == 2);
    }


    [Fact]
    public async Task CancellationSignal_ShouldCancel_AllComputedSignals()
    {
        await this.SwitchToMainThread();
        var cancellationRequested = new Signal<bool>();

        var waitForCancellationSignal = new Signal<bool>(false);
        var cancellationToken = new Signal<CancellationToken?>();
        var computedSignal = ComputedSignalFactory.Default
                                                  .DisconnectEverythingWhen(cancellationRequested)
                                                  .AsyncComputed(async token =>
                                                  {
                                                      await waitForCancellationSignal.FirstAsync(x => x);
                                                      cancellationToken.Value = token;
                                                      return 1;
                                                  }, 0);

        _ = computedSignal.Value;
        cancellationRequested.Value = true;
        waitForCancellationSignal.Value = true;

        await cancellationToken.FirstAsync(x => x is not null);
        cancellationToken.Value!.Value.IsCancellationRequested.Should().Be(true);

        cancellationToken.Value = null;
        cancellationRequested.Value = false;

        await cancellationToken.FirstAsync(x => x is not null);
        cancellationToken.Value!.Value.IsCancellationRequested.Should().BeFalse();
    }
}