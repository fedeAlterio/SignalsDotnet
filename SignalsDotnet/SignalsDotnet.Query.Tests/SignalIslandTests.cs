using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class SignalIslandTests
{
    const int TestTimeoutMs = 20_000;

    static CancellationTokenSource Timeout() => new(TestTimeoutMs);

    static SignalIsland<T> Island<T>(Func<CancellationToken, ValueTask<T>> factory)
    {
        var ambient = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            return new SignalIsland<T>(factory);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(ambient);
        }
    }

    sealed class Box
    {
        public int Value;
        public int MaxConcurrency;
        int _concurrent;

        public void Enter()
        {
            var current = Interlocked.Increment(ref _concurrent);

            if (current > MaxConcurrency)
                MaxConcurrency = current;

            Interlocked.Decrement(ref _concurrent);
        }
    }

    [Fact]
    public async Task InvokeAsync_RunsTheActionAgainstTheFactoryValue()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box { Value = 7 }));
        using var timeout = Timeout();

        var observed = 0;
        await island.InvokeAsync(box => observed = box.Value, timeout.Token);

        observed.ShouldBe(7);
    }

    [Fact]
    public async Task TheFactory_RunsOnlyOnce()
    {
        var created = 0;
        var island = Island<Box>(_ =>
        {
            Interlocked.Increment(ref created);
            return new ValueTask<Box>(new Box());
        });
        using var timeout = Timeout();

        for (var i = 0; i < 10; i++)
            await island.InvokeAsync(_ => { }, timeout.Token);

        created.ShouldBe(1);
    }

    [Fact]
    public async Task TheSameValue_IsHandedToEveryInvocation()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        Box? first = null;
        Box? second = null;

        await island.InvokeAsync(box => first = box, timeout.Token);
        await island.InvokeAsync(box => second = box, timeout.Token);

        first.ShouldNotBeNull();
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task ConcurrentInvocations_AreSerialized()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
                await island.InvokeAsync(box =>
                {
                    box.Enter();
                    box.Value++;
                }, timeout.Token);
        })));

        var value = 0;
        var maxConcurrency = 0;

        await island.InvokeAsync(box =>
        {
            value = box.Value;
            maxConcurrency = box.MaxConcurrency;
        }, timeout.Token);

        value.ShouldBe(64 * 20);
        maxConcurrency.ShouldBe(1);
    }

    [Fact]
    public async Task AsyncActions_AreAwaited()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        await island.InvokeAsync(async box =>
        {
            await Task.Yield();
            box.Value = 42;
        }, timeout.Token);

        var observed = 0;
        await island.InvokeAsync(box => observed = box.Value, timeout.Token);

        observed.ShouldBe(42);
    }

    [Fact]
    public async Task ExceptionsFromTheAction_ReachTheCaller()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await island.InvokeAsync(_ => throw new InvalidOperationException("boom"), timeout.Token));

        exception.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task ExceptionsFromTheFactory_ReachTheCaller()
    {
        var island = Island<Box>(_ => throw new InvalidOperationException("factory failed"));
        using var timeout = Timeout();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await island.InvokeAsync(_ => { }, timeout.Token));

        exception.Message.ShouldBe("factory failed");
    }

    [Fact]
    public async Task AnAlreadyCancelledToken_CancelsTheInvocation()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await island.InvokeAsync(_ => { }, cancelled.Token));
    }

    [Fact]
    public async Task NullAction_Throws()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));

        await Should.ThrowAsync<ArgumentNullException>(async () => await island.InvokeAsync((Action<Box>)null!));
        await Should.ThrowAsync<ArgumentNullException>(async () => await island.InvokeAsync((Func<Box, ValueTask>)null!));
    }

    [Fact]
    public void NullFactory_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SignalIsland<Box>(null!));
    }

    [Fact]
    public void UnhandledError_DefaultsToAConsoleLogger()
    {
        SignalIsland.UnhandledError.ShouldNotBeNull();
    }

    [Fact]
    public async Task SwitchToIslandContext_ReturnsTheIslandValue()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box { Value = 9 }));
        using var timeout = Timeout();

        var box = await island.SwitchToIslandContextAsync(timeout.Token);

        box.Value.ShouldBe(9);
    }

    [Fact]
    public async Task SwitchToIslandContext_RunsContinuationsOnTheIsland()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        await island.SwitchToIslandContextAsync(timeout.Token);

        var onIsland = false;
        await island.InvokeAsync(_ => onIsland = SynchronizationContext.Current is not null, timeout.Token);

        onIsland.ShouldBeTrue();
        Thread.CurrentThread.IsThreadPoolThread.ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchToIslandContext_SerializesWithInvocations()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            var box = await island.SwitchToIslandContextAsync(timeout.Token);

            box.Enter();
            box.Value++;
        })));

        var result = await island.InvokeAsync(box => (box.Value, box.MaxConcurrency), timeout.Token);

        result.Value.ShouldBe(32);
        result.MaxConcurrency.ShouldBe(1);
    }

    [Fact]
    public async Task SwitchToIslandContext_WhenAlreadyOnTheIsland_CompletesSynchronously()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box { Value = 3 }));
        using var timeout = Timeout();

        var completedSynchronously = false;
        var value = 0;

        await island.InvokeAsync(_ =>
        {
            var awaiter = island.SwitchToIslandContextAsync(timeout.Token).GetAwaiter();

            completedSynchronously = awaiter.IsCompleted;
            value = awaiter.GetResult().Value;
        }, timeout.Token);

        completedSynchronously.ShouldBeTrue();
        value.ShouldBe(3);
    }

    [Fact]
    public async Task SwitchToIslandContext_FromOffTheIsland_DoesNotCompleteSynchronously()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        island.SwitchToIslandContextAsync(timeout.Token).GetAwaiter().IsCompleted.ShouldBeFalse();

        await island.InvokeAsync(_ => { }, timeout.Token);
    }

    [Fact]
    public async Task SwitchToIslandContext_PropagatesFactoryExceptions()
    {
        var island = Island<Box>(_ => throw new InvalidOperationException("factory failed"));
        using var timeout = Timeout();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await island.SwitchToIslandContextAsync(timeout.Token));

        exception.Message.ShouldBe("factory failed");
    }

    [Fact]
    public async Task SwitchToIslandContext_AThrowingContinuationFaultsTheAwaitingTask()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        var faulted = Task.Run(async () =>
        {
            await island.SwitchToIslandContextAsync(timeout.Token);
            throw new InvalidOperationException("continuation failed");
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await faulted.WaitAsync(timeout.Token));

        exception.Message.ShouldBe("continuation failed");
    }

    [Fact]
    public async Task SwitchToIslandContext_ObservesCancellation()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await island.SwitchToIslandContextAsync(cancelled.Token));
    }

    [Fact]
    public async Task SyncResult_IsReturnedToTheCaller()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box { Value = 11 }));
        using var timeout = Timeout();

        var result = await island.InvokeAsync(box => box.Value, timeout.Token);

        result.ShouldBe(11);
    }

    [Fact]
    public async Task AsyncResult_IsReturnedToTheCaller()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box { Value = 13 }));
        using var timeout = Timeout();

        var result = await island.InvokeAsync(async box =>
        {
            await Task.Yield();
            return box.Value * 2;
        }, timeout.Token);

        result.ShouldBe(26);
    }

    [Fact]
    public async Task ResultOverloads_RunOnTheIsland()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            await island.InvokeAsync(box =>
            {
                box.Enter();
                return ++box.Value;
            }, timeout.Token))));

        var results = await island.InvokeAsync(box => (box.Value, box.MaxConcurrency), timeout.Token);

        results.Value.ShouldBe(32);
        results.MaxConcurrency.ShouldBe(1);
    }

    [Fact]
    public async Task ResultOverloads_PropagateExceptions()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));
        using var timeout = Timeout();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await island.InvokeAsync((Func<Box, int>)(_ => throw new InvalidOperationException("boom")), timeout.Token));
    }

    [Fact]
    public async Task NullResultAction_Throws()
    {
        var island = Island<Box>(_ => new ValueTask<Box>(new Box()));

        await Should.ThrowAsync<ArgumentNullException>(async () => await island.InvokeAsync((Func<Box, int>)null!));
        await Should.ThrowAsync<ArgumentNullException>(async () => await island.InvokeAsync((Func<Box, ValueTask<int>>)null!));
    }

    [Fact]
    public async Task ASlowFactory_RunsOnlyOnceUnderConcurrentInvocations()
    {
        var created = 0;
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var island = Island<Box>(async _ =>
        {
            Interlocked.Increment(ref created);
            await release.Task.ConfigureAwait(false);
            return new Box();
        });

        using var timeout = Timeout();

        var invocations = Enumerable.Range(0, 32)
                                    .Select(_ => island.InvokeAsync(box => box.Value++, timeout.Token).AsTask())
                                    .ToArray();

        release.SetResult(null);
        await Task.WhenAll(invocations);

        created.ShouldBe(1);

        var value = 0;
        await island.InvokeAsync(box => value = box.Value, timeout.Token);
        value.ShouldBe(32);
    }

    [Fact]
    public async Task AReentrantFactory_DoesNotRunTwice()
    {
        var created = 0;
        SignalIsland<Box>? island = null;
        Task? reentrant = null;

        island = Island<Box>(_ =>
        {
            Interlocked.Increment(ref created);
            reentrant = island!.InvokeAsync(box => box.Value++).AsTask();

            return new ValueTask<Box>(new Box());
        });

        using var timeout = Timeout();

        await island.InvokeAsync(_ => { }, timeout.Token);
        await reentrant!.WaitAsync(timeout.Token);

        created.ShouldBe(1);
    }

    [Fact]
    public async Task AFailedFactory_IsRetriedByTheNextInvocation()
    {
        var attempts = 0;

        var island = Island<Box>(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("first attempt fails");

            return new ValueTask<Box>(new Box { Value = 5 });
        });

        using var timeout = Timeout();

        await Should.ThrowAsync<InvalidOperationException>(async () => await island.InvokeAsync(_ => { }, timeout.Token));

        var value = 0;
        await island.InvokeAsync(box => value = box.Value, timeout.Token);

        value.ShouldBe(5);
        attempts.ShouldBe(2);
    }
}
