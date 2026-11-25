namespace SignalsDotnet.Internals;

internal static class BatchScope
{
    [ThreadStatic] static int _depth;
    [ThreadStatic] static BatchFlushMode _mode;
    [ThreadStatic] static List<Action>? _pending;
    [ThreadStatic] static List<Action>? _pendingComputations;
    [ThreadStatic] static Queue<Action>? _pendingEffects;

    public static bool IsActive => _depth > 0;

    public static void Enter(BatchFlushMode mode)
    {
        if (_depth++ == 0)
            _mode = mode;
    }

    public static void Defer(Action flush, FlushOrder order)
    {
        if (_mode == BatchFlushMode.InOrderOfSubscription)
        {
            (_pending ??= new()).Add(flush);
            return;
        }

        if (order == FlushOrder.WithComputations)
        {
            (_pendingComputations ??= new()).Add(flush);
            return;
        }

        (_pendingEffects ??= new()).Enqueue(flush);
    }

    public static void Exit()
    {
        if (_depth == 0)
            return;

        if (--_depth > 0)
            return;

        if (_mode == BatchFlushMode.ComputationsBeforeEffects)
        {
            FlushComputationsBeforeEffects();
            return;
        }

        FlushInOrderOfSubscription();
    }

    static void FlushInOrderOfSubscription()
    {
        var pending = _pending;
        if (pending is null || pending.Count == 0)
            return;

        _pending = null;

        foreach (var flush in pending)
        {
            flush();
        }
    }

    static void FlushComputationsBeforeEffects()
    {
        if (!AnyComputationOrEffectIsPending())
        {
            _mode = BatchFlushMode.InOrderOfSubscription;
            return;
        }

        _depth = 1;
        try
        {
            while (AnyComputationOrEffectIsPending())
            {
                SettleAllPendingComputations();
                RunTheNextPendingEffect();
            }
        }
        finally
        {
            _depth = 0;
            _mode = BatchFlushMode.InOrderOfSubscription;
        }
    }

    static bool AnyComputationOrEffectIsPending() => _pendingComputations is { Count: > 0 } || _pendingEffects is { Count: > 0 };

    static void SettleAllPendingComputations()
    {
        while (_pendingComputations is { Count: > 0 } wave)
        {
            _pendingComputations = null;
            foreach (var flush in wave)
            {
                flush();
            }
        }
    }

    static void RunTheNextPendingEffect()
    {
        if (_pendingEffects is { Count: > 0 } effects)
        {
            effects.Dequeue()();
        }
    }
}

internal enum FlushOrder
{
    WithComputations,
    AfterComputations
}
