namespace Cabbie.Aws.Tests;

public sealed class SnsEndpointTests
{
    [Theory]
    [InlineData("sns://orders/")]
    [InlineData("sns://orders")]
    [InlineData("SNS://Orders/")]
    public void a_topic_name_host_parses(string uri)
    {
        Assert.Equal("orders", SnsEndpoint.Parse(new Uri(uri)).TopicName);
    }

    [Theory]
    [InlineData("local://orders")]
    [InlineData("sqs://orders/")]
    [InlineData("sns://orders/extra")]
    [InlineData("sns://orders/?x=1")]
    [InlineData("sns://orders/#frag")]
    [InlineData("sns://bad_name/")]
    public void an_address_that_does_not_name_exactly_one_topic_is_rejected(string uri)
    {
        Assert.Throws<ArgumentException>(() => SnsEndpoint.Parse(new Uri(uri)));
    }

    [Fact]
    public void a_name_longer_than_seventy_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SnsEndpoint.Parse(new Uri($"sns://{new string('a', 71)}/")));
    }
}
