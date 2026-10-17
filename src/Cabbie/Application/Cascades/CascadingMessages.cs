using System.Collections;
using System.Runtime.CompilerServices;
using Cabbie.Domain.Sagas;

namespace Cabbie.Application.Cascades;

/// <summary>
/// Unpack a handler return value into outgoing message bodies. Emit only after success.
/// </summary>
public static class CascadingMessages
{
    public static IReadOnlyList<object> From(object? handlerResult)
    {
        var outgoing = new List<object>();
        Append(outgoing, handlerResult);
        return outgoing;
    }

    private static void Append(List<object> outgoing, object? value)
    {
        switch (value)
        {
            case null:
            case Saga:
            case Domain.Envelope.Envelope:
                return;
            case Task task:
                AppendCompletedTask(outgoing, task);
                return;
            case ValueTask valueTask:
                // Non-consuming AsTask: sync results and completed Task-backed values
                // unpack here; incomplete values are skipped by AppendCompletedTask,
                // mirroring the Task arm. The Executor awaits ValueTask returns before
                // this point, so the handler path always arrives complete.
                AppendCompletedTask(outgoing, TaskResultAccessor.AsTask(valueTask));
                return;
            case string:
                outgoing.Add(value);
                return;
            case OutgoingMessages messages:
                foreach (object message in messages)
                {
                    Append(outgoing, message);
                }

                return;
            case ITuple tuple:
                for (int i = 0; i < tuple.Length; i++)
                {
                    Append(outgoing, tuple[i]);
                }

                return;
            case IEnumerable enumerable:
                foreach (object? item in enumerable)
                {
                    Append(outgoing, item);
                }

                return;
            default:
                // ValueTask<T> and ValueTask are unrelated structs, so a boxed generic
                // ValueTask cannot match a case pattern; route it via the runtime type.
                if (TaskResultAccessor.IsBoxedValueTask(value))
                {
                    AppendCompletedTask(outgoing, TaskResultAccessor.AsTask(value));
                    return;
                }

                outgoing.Add(value);
                return;
        }
    }

    private static void AppendCompletedTask(List<object> outgoing, Task task)
    {
        if (task is not { IsCompletedSuccessfully: true })
        {
            return;
        }

        Type taskType = task.GetType();
        if (!taskType.IsGenericType)
        {
            return;
        }

        Type resultType = taskType.GetGenericArguments()[0];
        if (!resultType.IsPublic)
        {
            return;
        }

        object? result = TaskResultAccessor.GetResult(task);
        Append(outgoing, result);
    }
}
