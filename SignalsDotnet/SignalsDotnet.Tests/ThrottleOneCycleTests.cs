using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using FluentAssertions;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class ThrottleOneCycleTests
{
    [Fact]
    public async Task ThrottleOneCycleShouldNotNotifyInlineCalls()
    {
        await this.SwitchToMainThread();
        var subject = new Subject<int>();
        int totalNotifications = 0;
        subject.ThrottleOneCycle(new SynchronizationContextScheduler(SynchronizationContext.Current!))
               .Subscribe(_ => Interlocked.Increment(ref totalNotifications));

        for (var i = 0; i < 1000; i++)
        {
            subject.OnNext(i);
        }

        totalNotifications.Should().Be(0);
        await Task.Yield();
        totalNotifications.Should().Be(1);
        await Task.Yield();
        totalNotifications.Should().Be(1);
    }
}

