namespace SignalsDotnet.SignalsStore.Tests.Helpers;

static class TestHelpers
{
    public static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");

            await Task.Yield();
        }
    }
}
