using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Application.Sagas;

/// <summary>
/// Load and success-replace saga instances by type and id. Persistence owns durable rows later.
/// </summary>
public interface ISagaStore
{
    Saga? Load(Type sagaType, SagaId id);

    void Save(Type sagaType, SagaId id, Saga instance);
}
