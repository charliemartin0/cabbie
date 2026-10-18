namespace Cabbie.Transports.Contracts;

/// <summary>Polling waits. A poll against a deadline, never a fixed sleep.</summary>
internal static class Eventually
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, TimeProvider time, string what)
    {
        long start = time.GetTimestamp();
        while (!await condition())
        {
            if (time.GetElapsedTime(start) >= timeout)
            {
                throw new TimeoutException($"Timed out after {timeout} waiting until {what}.");
            }

            await Task.Delay(Interval, time);
        }
    }

    public static async Task NeverWithinAsync(Func<Task<bool>> condition, TimeSpan window, TimeProvider time, string what)
    {
        long start = time.GetTimestamp();
        while (time.GetElapsedTime(start) < window)
        {
            if (await condition())
            {
                Assert.Fail($"Expected never within {window}: {what}.");
            }

            await Task.Delay(Interval, time);
        }

        if (await condition())
        {
            Assert.Fail($"Expected never within {window}: {what}.");
        }
    }
}
