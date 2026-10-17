namespace Cabbie.Domain.Sagas;

/// <summary>
/// Optional delay on a message type. When absent, the message is not scheduled.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class TimeoutAttribute : Attribute
{
    public int Hours { get; init; }
    public int Minutes { get; init; }
    public int Seconds { get; init; }
    public int Milliseconds { get; init; }

    public TimeSpan Delay => new(0, Hours, Minutes, Seconds, Milliseconds);
}
