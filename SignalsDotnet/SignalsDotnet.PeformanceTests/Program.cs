using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using R3;
using SignalsDotnet;
using SignalsDotnet.Tests.Helpers;

BenchmarkRunner.Run<ComputedBenchmarks>();

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddJob(Job.MediumRun
                  .WithToolchain(InProcessNoEmitToolchain.Instance));
    }
}

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ComputedBenchmarks
{
    readonly Signal<int> _signal = new(0);
    readonly IAsyncReadOnlySignal<int> _asyncComputed;
    readonly IReadOnlySignal<int> _computed;

    public ComputedBenchmarks()
    {
        _computed = Signal.Computed(() => _signal.Value, x => x with{SubscribeWeakly = false});
        _asyncComputed = Signal.AsyncComputed(async _ =>
        {
            var x = _signal.Value;
            await Task.Yield();
            return x;
        }, -1);
    }

    [Benchmark]
    public int ComputedRoundTrip()
    {
        _ = _computed.Value;
        _signal.Value = 0;
        _signal.Value = 1;
        return _computed.Value;
    }

    [Benchmark]
    public async ValueTask<int> AsyncComputedRoundTrip()
    {
        await this.SwitchToMainThread();

        _ = _asyncComputed.Value;
        _signal.Value = 0;
        _signal.Value = 1;
        return await _asyncComputed.Values
                                   .FirstAsync(x => x == 1)
                                   .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
    }
}
