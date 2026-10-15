using Cabbie.Domain.Messaging;
using Cabbie.Domain.Sagas;

namespace Cabbie.Tests.Domain;

public sealed class OrderSaga : Saga
{
    public int? Id { get; set; }
}

public sealed record PlaceOrder([property: SagaIdentity] int OrderId);

public sealed record ChargePayment(int OrderId);

[Timeout(Minutes = 1)]
public sealed record OrderTimeout([property: SagaIdentity] int OrderId);

public sealed record MessageWithOrderSagaId(int OrderSagaId);

public sealed record MessageWithId(int Id);

public sealed record AttributedOverConvention
{
    [SagaIdentity]
    public int OrderId { get; init; }

    public int OrderSagaId { get; init; }

    public int Id { get; init; }
}

[MessageIdentity("place-order")]
public sealed record AliasedPlaceOrder(int OrderId);

[MessageIdentity("shared-alias")]
public sealed record FirstSharedAlias;

[MessageIdentity("shared-alias")]
public sealed record SecondSharedAlias;

[MessageIdentity("")]
public sealed record EmptyAliasMessage;

public sealed record UnaliasedMessage;
