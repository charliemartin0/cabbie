using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Cabbie.Application.Cascades;

/// <summary>
/// Cached, compiled access to async handler results. The <see cref="Task"/> side unwraps
/// <c>Task&lt;T&gt;.Result</c> keyed by the closed runtime task type; the
/// <see cref="ValueTask"/> side converts a boxed ValueTask (generic or not) to its
/// backing task via <c>AsTask()</c>, compiled once per closed runtime type. No
/// GetProperty reflection per call.
/// </summary>
internal static class TaskResultAccessor
{
    internal static ConcurrentDictionary<Type, Func<Task, object?>> Cache { get; } = new();

    internal static ConcurrentDictionary<Type, Func<object, Task>> ValueTaskToTaskCache { get; } = new();

    public static object? GetResult(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        Type taskType = task.GetType();
        if (!taskType.IsGenericType)
        {
            return null;
        }

        return Cache.GetOrAdd(taskType, Compile)(task);
    }

    /// <summary>
    /// A boxed <see cref="ValueTask"/> or <see cref="ValueTask{T}"/>. Pattern matching
    /// cannot catch the generic form (ValueTask&lt;T&gt; and ValueTask are unrelated
    /// structs), so the runtime type is inspected.
    /// </summary>
    internal static bool IsBoxedValueTask(object? value) =>
        value is not null
        && (value.GetType() == typeof(ValueTask)
            || (value.GetType().IsGenericType
                && value.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>)));

    /// <summary>
    /// Non-consuming conversion of a boxed ValueTask to its backing task. Sync results
    /// produce a completed task; source-backed values produce a wrapper; Task-backed
    /// values are reused as-is. The caller awaits before reading the result.
    /// </summary>
    public static Task AsTask(object boxedValueTask)
    {
        ArgumentNullException.ThrowIfNull(boxedValueTask);
        return ValueTaskToTaskCache.GetOrAdd(boxedValueTask.GetType(), CompileToTask)(boxedValueTask);
    }

    private static Func<Task, object?> Compile(Type taskType)
    {
        ParameterExpression taskParameter = Expression.Parameter(typeof(Task), "task");
        Expression instance = Expression.Convert(taskParameter, taskType);
        PropertyInfo resultProperty = taskType.GetProperty(nameof(Task<object>.Result))
            ?? throw new InvalidOperationException($"'{taskType}' has no Result property.");
        Expression body = Expression.Convert(Expression.Property(instance, resultProperty), typeof(object));
        return Expression.Lambda<Func<Task, object?>>(body, taskParameter).Compile();
    }

    private static Func<object, Task> CompileToTask(Type valueTaskType)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(object), "boxedValueTask");
        Expression instance = Expression.Convert(parameter, valueTaskType);
        MethodInfo asTask = valueTaskType.GetMethod(nameof(ValueTask.AsTask), Type.EmptyTypes)
            ?? throw new InvalidOperationException($"'{valueTaskType}' has no AsTask method.");
        return Expression.Lambda<Func<object, Task>>(
                Expression.Call(instance, asTask),
                parameter)
            .Compile();
    }
}