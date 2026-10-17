using System.Reflection;
using Cabbie.Application.Sagas;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Infrastructure.Sagas;

/// <summary>
/// In-memory snapshots. Save and Load shallow-clone so a throwing Handle cannot dirty the stored instance.
/// </summary>
public sealed class InMemorySagaStore : ISagaStore
{
    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly Dictionary<(Type SagaType, string Id), Saga> _rows = [];

    public Saga? Load(Type sagaType, SagaId id)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(id);

        return _rows.TryGetValue((sagaType, id.Value), out Saga? stored) ? Clone(stored) : null;
    }

    public void Save(Type sagaType, SagaId id, Saga instance)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(instance);

        _rows[(sagaType, id.Value)] = Clone(instance);
    }

    private static Saga Clone(Saga source) => (Saga)MemberwiseCloneMethod.Invoke(source, null)!;
}
