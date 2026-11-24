using System.Runtime.CompilerServices;
using FluentAssertions;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class SignalsRequestedScopeTests
{
    [Fact(Timeout = 20_000)]
    public async Task AnEffectDoesNotRerunWhenTheComputedItReadsKeepsItsValue()
    {
        await this.SwitchToMainThread();
        var temperature = new Signal<double>(101);
        var isAboveLimit = Signal.Computed(() => temperature.Value > 100);
        var runs = 0;
        using var effect = new Effect(() =>
        {
            _ = isAboveLimit.Value;
            runs++;
        });

        temperature.Value = 102;

        runs.Should().Be(1);
    }

    [Fact(Timeout = 20_000)]
    public async Task AComputedEvaluatedInAContextCapturedDuringAnEffectIsNotKeptAlive()
    {
        await this.SwitchToMainThread();
        var trigger = new Signal<int>();
        ExecutionContext? captured = null;
        using (new Effect(() =>
               {
                   _ = trigger.Value;
                   captured = ExecutionContext.Capture();
               }))
        {
        }

        var computed = EvaluateAComputedInside(captured!);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        computed.IsAlive.Should().BeFalse();
        GC.KeepAlive(captured);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference EvaluateAComputedInside(ExecutionContext context)
    {
        WeakReference? computed = null;
        ExecutionContext.Run(context, _ =>
        {
            var source = new Signal<int>(1);
            var signal = Signal.Computed(() => source.Value + 1);
            _ = signal.Value;
            computed = new WeakReference(signal);
        }, null);

        return computed!;
    }
}
