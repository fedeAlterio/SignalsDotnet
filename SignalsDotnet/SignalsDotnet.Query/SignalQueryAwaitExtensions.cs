namespace SignalsDotnet.Query;

public static class SignalQueryAwaitExtensions
{
    public static T Await<T>(this Task<T> task) => throw NotQueryable();

    public static T Await<T>(this ValueTask<T> task) => throw NotQueryable();

    static InvalidOperationException NotQueryable() =>
        new($"'{nameof(Await)}' can only be used inside a query expression.");
}
