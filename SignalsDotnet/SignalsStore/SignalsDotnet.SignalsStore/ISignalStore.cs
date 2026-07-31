using SignalsDotnet.Configuration;

namespace SignalsDotnet.SignalsStore;

public delegate ReadonlySignalConfiguration SignalProxyConfigurationDelegate(string id, ReadonlySignalConfiguration configuration);

public interface ISignalStore
{
    ISignalProxy<T> CreateSignalProxy<T>(string id, T startValue);
}