namespace Cabbie.Domain.Sagas.ValueObjects;

/// <summary>
/// Which saga instance a message belongs to. Empty when it is not part of a saga. Envelope carries this.
/// </summary>
public record SagaId(string Value);
