namespace SignalsDotnet.Internals;

internal static class BatchScope
{
    [ThreadStatic] static int _depth;
    [ThreadStatic] static List<Action>? _pending;

    public static bool IsActive => _depth > 0;

    public static void Enter() => _depth++;

    public static void Defer(Action flush) => (_pending ??= new()).Add(flush);

    public static void Exit()
    {
        if (_depth == 0)
            return;

        if (--_depth > 0)
            return;

        if (_pending is null || _pending.Count == 0)
            return;

        _depth = 1;
        try
        {
            while (_pending is { Count: > 0 } pending)
            {
                _pending = null;
                foreach (var flush in pending)
                {
                    flush();
                }
            }
        }
        finally
        {
            _depth = 0;
        }
    }
}
