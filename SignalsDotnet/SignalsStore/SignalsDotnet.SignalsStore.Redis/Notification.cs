using System.Text.Json;
using R3Async;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore.Redis;

sealed record Notification<T>
{
    public T? Value { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsCompletedSuccessfully { get; init; }

    public static Notification<T> ForValue(T value) => new() { Value = value };

    public static Notification<T> ForError(Exception error) => new() { ErrorMessage = error.Message };

    public static Notification<T> ForCompletion(Result result) => new()
    {
        IsCompleted = true,
        IsCompletedSuccessfully = result.IsSuccess,
        ErrorMessage = result.IsFailure ? result.Exception.Message : null,
    };

    public ValueTask ForwardTo(AsyncObserver<T> observer, CancellationToken cancellationToken) => this switch
    {
        { IsCompleted: true, IsCompletedSuccessfully: true } =>
            observer.OnCompletedAsync(Result.Success),
        { IsCompleted: true, ErrorMessage: { } message } =>
            observer.OnCompletedAsync(Result.Failure(new SignalsStoreRedisException(message))),
        { ErrorMessage: { } message } =>
            observer.OnErrorResumeAsync(new SignalsStoreRedisException(message), cancellationToken),
        _ => observer.OnNextAsync(Value!, cancellationToken),
    };
}
