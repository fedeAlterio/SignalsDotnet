using System.Runtime.CompilerServices;

namespace SignalsDotnet.Tests.Helpers;
internal static class TestHelpers
{
    public static async Task WaitUntil(Func<bool> predicate)
    {
        while (!predicate())
        {
            await Task.Yield();
        }
    }
}
