namespace Cabbie.Aws.Tests;

public sealed class SqsEndpointTests
{
    [Theory]
    [InlineData("sqs://orders/")]
    [InlineData("sqs://orders")]
    [InlineData("SQS://Orders/")]
    public void a_queue_name_host_parses_and_names_its_dead_letter_queue(string uri)
    {
        SqsEndpoint endpoint = SqsEndpoint.Parse(new Uri(uri));

        Assert.Equal("orders", endpoint.QueueName);
        Assert.Equal("orders-dead-letter", endpoint.DeadLetterQueueName);
    }

    [Theory]
    [InlineData("local://orders")]
    [InlineData("sqs://orders/extra")]
    [InlineData("sqs://orders/?x=1")]
    [InlineData("sqs://orders/#frag")]
    [InlineData("sqs://bad_name/")]
    public void an_address_that_does_not_name_exactly_one_queue_is_rejected(string uri)
    {
        Assert.Throws<ArgumentException>(() => SqsEndpoint.Parse(new Uri(uri)));
    }

    [Fact]
    public void a_name_longer_than_seventy_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SqsEndpoint.Parse(new Uri($"sqs://{new string('a', 71)}/")));
    }
}
