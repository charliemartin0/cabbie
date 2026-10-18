using Amazon.SQS;
using Amazon.SQS.Model;

namespace Cabbie.Aws;

internal static class SqsQueues
{
    public static async Task<string> ResolveAsync(IAmazonSQS client, string queueName)
    {
        GetQueueUrlResponse response = await client.GetQueueUrlAsync(queueName);
        return response.QueueUrl;
    }
}
