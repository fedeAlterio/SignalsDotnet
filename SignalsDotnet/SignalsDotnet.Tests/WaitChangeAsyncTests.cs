using FluentAssertions;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class WaitChangeAsyncTests
{
    const int TestTimeoutMs = 10_000;

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldReturnTheValueAfterTheChange()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);

        var awaitable = Signal.WaitForChangeAsync(() => number.Value * 10);
        number.Value = 5;

        var result = await awaitable;
        result.Should().Be(50);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldNotCompleteBeforeAChange()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);

        var awaitable = Signal.WaitForChangeAsync(() => number.Value);
        var awaiter = awaitable.GetAwaiter();

        awaiter.IsCompleted.Should().BeFalse();

        number.Value = 2;
        awaiter.IsCompleted.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldOnlyTrackReadSignals()
    {
        await this.SwitchToMainThread();
        var tracked = new Signal<int>(1);
        var untracked = new Signal<int>(1);

        var awaitable = Signal.WaitForChangeAsync(() => tracked.Value);
        var awaiter = awaitable.GetAwaiter();

        untracked.Value = 99;
        awaiter.IsCompleted.Should().BeFalse();

        tracked.Value = 2;
        awaiter.IsCompleted.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldReevaluateTheFuncAfterTheChange()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);
        var evaluations = 0;

        var awaitable = Signal.WaitForChangeAsync(() =>
        {
            evaluations++;
            return number.Value;
        });

        evaluations.Should().Be(1);

        number.Value = 7;

        var result = await awaitable;
        result.Should().Be(7);
        evaluations.Should().Be(2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldNotLeakDependenciesIntoAnEnclosingComputed()
    {
        await this.SwitchToMainThread();
        var waited = new Signal<int>(1);
        var outerSource = new Signal<int>(1);
        var outerEvaluations = 0;

        var computed = Signal.Computed(() =>
        {
            outerEvaluations++;
            Signal.WaitForChangeAsync(() => waited.Value);
            return outerSource.Value;
        });

        _ = computed.Value;
        var evaluationsAfterFirstCompute = outerEvaluations;

        waited.Value = 2;

        outerEvaluations.Should().Be(evaluationsAfterFirstCompute);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldThrowWhenCancelled()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);
        using var cts = new CancellationTokenSource();

        var awaitable = Signal.WaitForChangeAsync(() => number.Value, cts.Token);
        cts.Cancel();

        var act = async () => await awaitable;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldThrowWhenAlreadyCancelled()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var awaitable = Signal.WaitForChangeAsync(() => number.Value, cts.Token);

        var act = async () => await awaitable;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task CancellationExceptionShouldCarryTheToken()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);
        using var cts = new CancellationTokenSource();

        var awaitable = Signal.WaitForChangeAsync(() => number.Value, cts.Token);
        cts.Cancel();

        var act = async () => await awaitable;
        var assertion = await act.Should().ThrowAsync<OperationCanceledException>();
        assertion.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldNotThrowWhenCancelledAfterTheChange()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);
        using var cts = new CancellationTokenSource();

        var awaitable = Signal.WaitForChangeAsync(() => number.Value, cts.Token);
        number.Value = 2;
        cts.Cancel();

        var result = await awaitable;
        result.Should().Be(2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldPropagateExceptionFromTheFirstEvaluation()
    {
        await this.SwitchToMainThread();

        var act = () => Signal.WaitForChangeAsync<int>(() => throw new InvalidOperationException("boom"));
        act.Should().Throw<InvalidOperationException>().WithMessage("boom");

        await Task.CompletedTask;
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldPropagateExceptionFromTheSecondEvaluation()
    {
        await this.SwitchToMainThread();
        var number = new Signal<int>(1);
        var first = true;

        var awaitable = Signal.WaitForChangeAsync(() =>
        {
            if (first)
            {
                first = false;
                return number.Value;
            }

            throw new InvalidOperationException("boom");
        });

        number.Value = 2;

        var act = async () => await awaitable;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
