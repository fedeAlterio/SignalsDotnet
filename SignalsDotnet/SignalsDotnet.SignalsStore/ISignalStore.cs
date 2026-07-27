using R3Async.Subjects;
using SignalsDotnet.Configuration;

namespace SignalsDotnet.SignalsStore;

public delegate ReadonlySignalConfiguration SignalProxyConfigurationDelegate(string id, ReadonlySignalConfiguration configuration);

public interface ISignalStore
{
    ISignalProxy<T> CreateSignalProxy<T>(string id, T startValue);
}

public interface ISubjectStore
{
    ISubject<T> CreateSubject<T>(string id);
}