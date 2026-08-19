using System.Reflection;
using Microsoft.AspNetCore.Http;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public sealed class SignalComputedQueryString
{
    SignalComputedQueryString(SignalComputedQuery query) => Query = query;

    public const string ParameterName = "query";

    public SignalComputedQuery Query { get; }

    public static async ValueTask<SignalComputedQueryString?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(context);

        var name = parameter?.Name is { Length: > 0 } candidate ? candidate : ParameterName;

        var raw = context.Request.Query[name].ToString();

        if (string.IsNullOrWhiteSpace(raw))
            return await BadRequest(context, $"A '{name}' parameter is required.");

        if (!SignalComputedQuery.TryParse(raw, out var query))
            return await BadRequest(context, $"'{raw}' is not a valid query.");

        return new SignalComputedQueryString(query);
    }

    static async ValueTask<SignalComputedQueryString?> BadRequest(HttpContext context, string error)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        await context.Response.WriteAsJsonAsync<object>(new { error });

        return null;
    }
}
