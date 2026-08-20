using System.Reflection;

namespace SignalsDotnet.Query.Internals;

static class ProjectionAsync
{
    internal static readonly MethodInfo FromResultMethod = typeof(ProjectionAsync).GetMethod(nameof(FromResult), BindingFlags.Public | BindingFlags.Static)!;
    internal static readonly MethodInfo FromTaskMethod = typeof(ProjectionAsync).GetMethod(nameof(FromTask), BindingFlags.Public | BindingFlags.Static)!;
    internal static readonly MethodInfo MapMethod = typeof(ProjectionAsync).GetMethod(nameof(Map), BindingFlags.Public | BindingFlags.Static)!;
    internal static readonly MethodInfo BindMethod = typeof(ProjectionAsync).GetMethod(nameof(Bind), BindingFlags.Public | BindingFlags.Static)!;
    internal static readonly MethodInfo WhenAllMethod = typeof(ProjectionAsync).GetMethod(nameof(WhenAll), BindingFlags.Public | BindingFlags.Static)!;
    internal static readonly MethodInfo SequenceMethod = typeof(ProjectionAsync).GetMethod(nameof(Sequence), BindingFlags.Public | BindingFlags.Static)!;
    internal static readonly MethodInfo DictionaryMethod = typeof(ProjectionAsync).GetMethod(nameof(Dictionary), BindingFlags.Public | BindingFlags.Static)!;

    public static ValueTask<T> FromResult<T>(T value) => new(value);

    public static ValueTask<T> FromTask<T>(Task<T> task) => new(task);

    public static async ValueTask<TResult> Map<T, TResult>(ValueTask<T> source, Func<T, TResult> selector) => selector(await source);

    public static async ValueTask<TResult> Bind<T, TResult>(ValueTask<T> source, Func<T, ValueTask<TResult>> selector) => await selector(await source);

    public static async ValueTask<object?> WhenAll(string[] keys, ValueTask<object?>[] values)
    {
        var result = new Dictionary<string, object?>(keys.Length, StringComparer.Ordinal);

        for (var i = 0; i < keys.Length; i++)
            result[keys[i]] = await values[i];

        return result;
    }

    public static async ValueTask<object?> Sequence<T>(IEnumerable<T>? source, Func<T, ValueTask<object?>> selector)
    {
        if (source is null)
            return null;

        var result = new List<object?>();

        foreach (var element in source)
            result.Add(await selector(element));

        return result;
    }

    public static async ValueTask<object?> Dictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>? source, Func<TValue, ValueTask<object?>> selector)
    {
        if (source is null)
            return null;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var pair in source)
            result[pair.Key?.ToString() ?? string.Empty] = await selector(pair.Value);

        return result;
    }
}
