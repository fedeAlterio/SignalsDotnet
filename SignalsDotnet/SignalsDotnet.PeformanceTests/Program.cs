using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using SignalsDotnet;

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
    readonly IReadOnlySignal<int> _computed;

    public ComputedBenchmarks()
    {
        _computed = Signal.Computed(() => _signal.Value, x => x with{SubscribeWeakly = false});
        _ = _computed.Value;
    }

    [Benchmark]
    public int ComputedRoundTrip()
    {
        _ = _computed.Value;
        _signal.Value = 0;
        _signal.Value = 1;
        return _computed.Value;
    }
}
