using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Testcontainers.LocalStack;

namespace Cabbie.Aws.Tests;

/// <summary>One LocalStack (SQS and SNS) container per test class. Dummy credentials: nothing real is ever used.</summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    static LocalStackFixture()
    {
        // Same Docker Desktop socket fallback as the Postgres fixture.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            string desktopSocket = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docker",
                "desktop",
                "docker.sock");
            if (Path.Exists(desktopSocket))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{desktopSocket}");
            }
        }
    }

    private readonly LocalStackContainer _container = new LocalStackBuilder("localstack/localstack:4.4")
        .WithEnvironment("SERVICES", "sqs,sns")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AmazonSQSClient CreateClient() =>
        new(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = _container.GetConnectionString(), AuthenticationRegion = "us-east-1" });

    public AmazonSimpleNotificationServiceClient CreateSnsClient() =>
        new(
            new BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig { ServiceURL = _container.GetConnectionString(), AuthenticationRegion = "us-east-1" });
}
