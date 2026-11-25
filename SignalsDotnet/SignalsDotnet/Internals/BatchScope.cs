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

        var pending = _pending;
        if (pending is null || pending.Count == 0)
            return;

        _pending = null;

        foreach (var flush in pending)
        {
            flush();
        }
    }
}
