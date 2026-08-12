namespace SignalsDotnet.Configuration;

public enum SubscriptionStrategy
{
    /// <summary>
    /// The signal subscribes to the source observable once, on the first value access, and keeps that
    /// subscription alive for its entire lifetime.
    /// </summary>
    Persistent,

    /// <summary>
    /// The signal subscribes to the source observable only while at least one observer is listening to
    /// <see cref="IReadOnlySignal{T}.Values"/> or <see cref="IReadOnlySignal{T}.FutureValues"/>, and
    /// unsubscribes from it once the last observer is gone (share + ref-count semantics).
    /// </summary>
    RefCount
}
