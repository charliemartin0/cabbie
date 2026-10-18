using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

namespace Cabbie.Aws;

internal static class SnsTopics
{
    /// <summary>SNS cannot look a topic up by name, so page the topic list for an ARN ending in <c>:name</c>.</summary>
    public static async Task<string> ResolveArnAsync(
        IAmazonSimpleNotificationService client,
        string topicName,
        CancellationToken cancellationToken)
    {
        string suffix = ":" + topicName;
        string? token = null;
        do
        {
            ListTopicsResponse response = await client.ListTopicsAsync(new ListTopicsRequest { NextToken = token }, cancellationToken);
            foreach (Topic topic in response.Topics ?? [])
            {
                if (topic.TopicArn.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return topic.TopicArn;
                }
            }

            token = response.NextToken;
        }
        while (!string.IsNullOrEmpty(token));

        throw new NotFoundException($"SNS topic '{topicName}' does not exist.");
    }
}
