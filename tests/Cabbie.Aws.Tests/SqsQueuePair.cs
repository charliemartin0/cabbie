using Amazon.SQS;
using Amazon.SQS.Model;

namespace Cabbie.Aws.Tests;

/// <summary>Creates a queue and its dead-letter queue, and deletes them when disposed.</summary>
internal sealed class SqsQueuePair
{
    private SqsQueuePair(string name, string queueUrl, string deadLetterUrl)
    {
        Name = name;
        QueueUrl = queueUrl;
        DeadLetterUrl = deadLetterUrl;
    }

    public string Name { get; }

    public string QueueUrl { get; }

    public string DeadLetterUrl { get; }

    public Uri Endpoint => new($"sqs://{Name}/");

    public static async Task<SqsQueuePair> CreateAsync(IAmazonSQS client)
    {
        string name = $"contract-{Guid.NewGuid():N}";
        string queue = (await client.CreateQueueAsync(name)).QueueUrl;
        string deadLetter = (await client.CreateQueueAsync(name + "-dead-letter")).QueueUrl;
        return new SqsQueuePair(name, queue, deadLetter);
    }

    public async Task DeleteAsync(IAmazonSQS client)
    {
        foreach (string url in new[] { QueueUrl, DeadLetterUrl })
        {
            try
            {
                await client.DeleteQueueAsync(url);
            }
            catch (QueueDoesNotExistException)
            {
                // A test may have deleted it already.
            }
        }
    }
}
