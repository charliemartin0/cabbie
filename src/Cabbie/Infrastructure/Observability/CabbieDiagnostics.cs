using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cabbie.Infrastructure.Observability;

/// <summary>
/// Shared BCL diagnostics surface for Cabbie. No-op by default: nothing is exported
/// until an external <see cref="ActivityListener"/> subscribes to <see cref="Source"/>
/// or an external <see cref="MeterListener"/> subscribes to instruments on <see cref="Meter"/>.
/// </summary>
public static class CabbieDiagnostics
{
    public const string SourceName = "Cabbie";
    public const string MeterName = "Cabbie";

    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("cabbie.failures");
}
