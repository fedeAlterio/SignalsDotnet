namespace SignalsDotnet.SignalsStore.SignalR;

/// <summary>
/// A single message flowing server -> client over the notification stream. IsSubscriptionAck
/// distinguishes the one-time acknowledgement that subject.Values.SubscribeAsync completed
/// server-side (or failed, via ErrorMessage) from the ordinary Value/Error/Completed traffic that
/// follows it - the client waits for this ack before treating the subscription as established.
/// </summary>
public sealed record SubjectNotification
{
    public bool IsSubscriptionAck { get; init; }
    public string? ValueJson { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsCompletedSuccessfully { get; init; }

    public static SubjectNotification SubscriptionAcknowledged() => new() { IsSubscriptionAck = true };

    public static SubjectNotification SubscriptionFailed(string errorMessage) => new()
    {
        IsSubscriptionAck = true,
        ErrorMessage = errorMessage,
    };

    public static SubjectNotification ForValue(string valueJson) => new() { ValueJson = valueJson };

    public static SubjectNotification ForError(string errorMessage) => new() { ErrorMessage = errorMessage };

    public static SubjectNotification ForCompletion(bool success, string? errorMessage) => new()
    {
        IsCompleted = true,
        IsCompletedSuccessfully = success,
        ErrorMessage = errorMessage,
    };
}
