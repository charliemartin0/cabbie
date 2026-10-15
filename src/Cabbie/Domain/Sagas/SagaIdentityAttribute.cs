namespace Cabbie.Domain.Sagas;

/// <summary>
/// Optional property that identifies which saga instance a message belongs to. When absent, SagaIdentityNaming uses {SagaType}Id then Id.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SagaIdentityAttribute : Attribute;
