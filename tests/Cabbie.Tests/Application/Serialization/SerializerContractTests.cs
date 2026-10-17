using Cabbie.Application.Serialization;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Serialization;

public sealed class SerializerContractTests
{
    [Fact]
    public void serialize_then_deserialize_round_trips_to_equivalent_object()
    {
        IMessageSerializer serializer = new JsonMessageSerializer();
        var original = new PlaceOrder(42);

        byte[] bytes = serializer.Serialize(original, typeof(PlaceOrder));
        var roundTripped = (PlaceOrder)serializer.Deserialize(bytes, typeof(PlaceOrder));

        Assert.Equal(original.OrderId, roundTripped.OrderId);
    }

    [Fact]
    public void json_serializer_declares_the_application_json_content_type()
    {
        IMessageSerializer serializer = new JsonMessageSerializer();

        Assert.Equal("application/json", serializer.ContentType);
    }

    [Fact]
    public void serialize_with_null_message_throws_argument_null_exception()
    {
        IMessageSerializer serializer = new JsonMessageSerializer();

        Assert.Throws<ArgumentNullException>(() => serializer.Serialize(null!, typeof(PlaceOrder)));
    }
}
