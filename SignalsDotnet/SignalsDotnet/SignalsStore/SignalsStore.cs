namespace SignalsDotnet.SignalsStore;

public class A
{
    public void Tmp()
    {
        ISignalsStore store = null!;
        var a = store.CreateSignalProxy(new("AAA"), 0);
        new Effect(async token =>
        {
            if (a.ConnectionState.Value is ConnectionState.Disconnected)
            {
                await a.ConnectAsync(CancellationToken.None);
            }
        });
        var b = Signal.Computed(() => a.Value + 1);
    }
}