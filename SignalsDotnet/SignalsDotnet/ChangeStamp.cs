namespace SignalsDotnet;

public readonly record struct ChangeStamp<T>(T Value, DateTimeOffset ChangedAt);
