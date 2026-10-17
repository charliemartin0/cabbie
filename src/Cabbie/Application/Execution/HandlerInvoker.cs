using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// Compiled handler invocation: creates (or receives) the handler instance, binds the
/// message / Envelope / CancellationToken slots and calls the handler method directly.
/// Returns the raw method result — a Task is still awaited by the Executor, exactly as
/// with the reflection path, so async and Task&lt;T&gt; handlers keep their current shape.
/// </summary>
internal delegate object? HandlerInvoker(
    object? target,
    object message,
    Envelope envelope,
    CancellationToken cancellationToken);