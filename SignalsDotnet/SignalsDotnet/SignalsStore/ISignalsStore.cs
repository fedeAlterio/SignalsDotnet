namespace SignalsDotnet.SignalsStore;

public interface ISignalsStore
{
    ISignalProxy<T> CreateSignalProxy<T>(SignalStoreKey key, T startValue);
}