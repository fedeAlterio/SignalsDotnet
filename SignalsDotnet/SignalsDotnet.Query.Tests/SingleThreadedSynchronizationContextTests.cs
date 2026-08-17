using SignalsDotnet.Query.Internals;
using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class SingleThreadedSynchronizationContextTests
{
    const int TestTimeoutMs = 20_000;

    static void Rethrow(Exception exception) => throw exception;

    static SynchronizationContext Create(Action<Exception> onError)
    {
        var ambient = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            return SingleThreadedSynchronizationContext.Create(onError);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(ambient);
        }
    }

    static void WaitUntil(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TestTimeoutMs);

        while (!condition())
        {
            cancellation.Token.ThrowIfCancellationRequested();
            Thread.Yield();
        }
    }

    [Fact]
    public void PostedCallbacks_RunInFifoOrder()
    {
        var context = Create(Rethrow);
        var order = new List<int>();

        for (var i = 0; i < 1000; i++)
        {
            var captured = i;
            context.Post(_ => order.Add(captured), null);
        }

        WaitUntil(() => order.Count == 1000);
        order.ShouldBe(Enumerable.Range(0, 1000));
    }

    [Fact]
    public void ConcurrentPosts_NeverRunCallbacksInParallel()
    {
        var context = Create(Rethrow);

        var concurrent = 0;
        var overlaps = 0;
        var executed = 0;

        Parallel.For(0, 16, _ =>
        {
            for (var i = 0; i < 200; i++)
                context.Post(_ =>
                {
                    if (Interlocked.Increment(ref concurrent) != 1)
                        Interlocked.Increment(ref overlaps);

                    Interlocked.Decrement(ref concurrent);
                    Interlocked.Increment(ref executed);
                }, null);
        });

        WaitUntil(() => Volatile.Read(ref executed) == 16 * 200);
        Volatile.Read(ref overlaps).ShouldBe(0);
    }

    [Fact]
    public void UnsynchronizedState_IsSafeAcrossConcurrentPosts()
    {
        var context = Create(Rethrow);
        var counter = 0;

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 500; i++)
                context.Post(_ => counter++, null);
        });

        WaitUntil(() => Volatile.Read(ref counter) == 8 * 500);
        counter.ShouldBe(4000);
    }

    [Fact]
    public void Callbacks_RunOnTheThreadPoolNotTheCaller()
    {
        var context = Create(Rethrow);

        var callbackThread = 0;
        var onThreadPool = false;

        context.Post(_ =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            onThreadPool = Thread.CurrentThread.IsThreadPoolThread;
        }, null);

        WaitUntil(() => Volatile.Read(ref callbackThread) != 0);

        callbackThread.ShouldNotBe(Environment.CurrentManagedThreadId);
        onThreadPool.ShouldBeTrue();
    }

    [Fact]
    public void CurrentContext_IsSetWhileCallbacksRun()
    {
        var context = Create(Rethrow);
        SynchronizationContext? observed = null;
        var ran = false;

        context.Post(_ =>
        {
            observed = SynchronizationContext.Current;
            Volatile.Write(ref ran, true);
        }, null);

        WaitUntil(() => Volatile.Read(ref ran));
        observed.ShouldBeSameAs(context);
    }

    [Fact]
    public void Send_BlocksUntilTheCallbackCompletes()
    {
        var context = Create(Rethrow);
        var ran = false;

        context.Send(_ => ran = true, null);

        ran.ShouldBeTrue();
    }

    [Fact]
    public void Send_PropagatesExceptionsToTheCaller()
    {
        var context = Create(Rethrow);

        Should.Throw<InvalidOperationException>(() => context.Send(_ => throw new InvalidOperationException("boom"), null))
              .Message.ShouldBe("boom");
    }

    [Fact]
    public void Send_FromInsideTheContext_RunsInlineWithoutDeadlocking()
    {
        var context = Create(Rethrow);
        var inner = false;
        var outer = false;

        context.Post(_ =>
        {
            context.Send(_ => inner = true, null);
            Volatile.Write(ref outer, true);
        }, null);

        WaitUntil(() => Volatile.Read(ref outer));
        inner.ShouldBeTrue();
    }

    [Fact]
    public void PostFromInsideACallback_IsProcessed()
    {
        var context = Create(Rethrow);
        var done = false;

        context.Post(_ => context.Post(_ => Volatile.Write(ref done, true), null), null);

        WaitUntil(() => Volatile.Read(ref done));
    }

    [Fact]
    public void AThrowingCallback_DoesNotStopTheQueue()
    {
        var errors = new List<Exception>();
        var context = Create(errors.Add);
        var ran = false;

        context.Post(_ => throw new InvalidOperationException("boom"), null);
        context.Post(_ => Volatile.Write(ref ran, true), null);

        WaitUntil(() => Volatile.Read(ref ran));
        errors.Select(x => x.Message).ShouldBe(["boom"]);
    }

    [Fact]
    public void NullErrorHandler_Throws()
    {
        Should.Throw<ArgumentNullException>(() => SingleThreadedSynchronizationContext.Create(null!));
    }
}
