using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.Validators;
using Cabbie.Domain.Messaging.ValueObjects;

namespace Cabbie.Tests.Domain.Messaging;

public sealed class MessageTypeCatalogTests
{
    [Fact]
    public void register_then_get_name_and_lookup_round_trip()
    {
        var catalog = new MessageTypeCatalog();
        catalog.Register(typeof(AliasedPlaceOrder));

        Assert.Equal("place-order", catalog.GetName(typeof(AliasedPlaceOrder)).Value);

        var lookup = catalog.Lookup(new MessageType("place-order"));
        var known = Assert.IsType<KnownMessageType>(lookup);
        Assert.Equal(typeof(AliasedPlaceOrder), known.ClrType);
    }

    [Fact]
    public void lookup_unknown_wire_name_is_a_result_not_an_exception()
    {
        var catalog = new MessageTypeCatalog();
        catalog.Register(typeof(UnaliasedMessage));

        var lookup = catalog.Lookup(new MessageType("not-registered"));

        var unknown = Assert.IsType<UnknownMessageType>(lookup);
        Assert.Equal("not-registered", unknown.Name.Value);
    }

    [Fact]
    public void get_name_for_unregistered_type_still_uses_naming()
    {
        var catalog = new MessageTypeCatalog();

        var name = catalog.GetName(typeof(UnaliasedMessage));

        Assert.Equal(MessageTypeNaming.For(typeof(UnaliasedMessage)), name);
        Assert.Empty(catalog.Registrations);
    }

    [Fact]
    public void registering_the_same_type_twice_is_a_noop()
    {
        var catalog = new MessageTypeCatalog();
        catalog.Register(typeof(UnaliasedMessage));
        catalog.Register(typeof(UnaliasedMessage));

        Assert.Single(catalog.Registrations);
        Assert.Equal(typeof(UnaliasedMessage), catalog.Registrations[0].Type);
    }

    [Fact]
    public void colliding_aliases_stay_on_the_list_and_lookup_keeps_the_first()
    {
        var catalog = new MessageTypeCatalog();
        catalog.Register(typeof(FirstSharedAlias));
        catalog.Register(typeof(SecondSharedAlias));

        Assert.Equal(2, catalog.Registrations.Count);
        Assert.Equal("shared-alias", catalog.GetName(typeof(FirstSharedAlias)).Value);
        Assert.Equal("shared-alias", catalog.GetName(typeof(SecondSharedAlias)).Value);

        var known = Assert.IsType<KnownMessageType>(catalog.Lookup(new MessageType("shared-alias")));
        Assert.Equal(typeof(FirstSharedAlias), known.ClrType);

        var result = new MessageTypeCatalogValidator().Validate(catalog);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void empty_identity_alias_fails_catalog_validation()
    {
        var catalog = new MessageTypeCatalog();
        catalog.Register(typeof(EmptyAliasMessage));

        var result = new MessageTypeCatalogValidator().Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void unique_registrations_pass_catalog_validation()
    {
        var catalog = new MessageTypeCatalog();
        catalog.Register(typeof(AliasedPlaceOrder));
        catalog.Register(typeof(UnaliasedMessage));

        var result = new MessageTypeCatalogValidator().Validate(catalog);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void register_null_throws()
    {
        var catalog = new MessageTypeCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.Register(null!));
        Assert.Throws<ArgumentNullException>(() => catalog.GetName(null!));
        Assert.Throws<ArgumentNullException>(() => catalog.Lookup(null!));
    }
}
