using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// Compiled handler invocation: binds the message / Envelope / CancellationToken slots,
/// resolves every other slot from <paramref name="services"/> (the message scope) and calls
/// the handler method directly on <paramref name="target"/>.
/// Returns the raw method result — a Task is still awaited by the Executor, exactly as
/// with the reflection path, so async and Task&lt;T&gt; handlers keep their current shape.
/// </summary>
internal delegate object? HandlerInvoker(
    object? target,
    object message,
    Envelope envelope,
    CancellationToken cancellationToken,
    IServiceProvider services);
