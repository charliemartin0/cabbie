using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Domain.Envelope;
using Microsoft.Extensions.DependencyInjection;

namespace Cabbie.Tests.Application.Execution;

/// <summary>
/// Prove-with for handler DI: constructor and method-parameter services resolve from one
/// scope per Executor.InvokeAsync, the scope is disposed on every exit, and a missing
/// service is reported with the handler and the service type.
/// </summary>
public sealed class ExecutorDependencyInjectionTests
{
    [Fact]
    public async Task scoped_dependency_is_a_fresh_instance_per_message()
    {
        var sink = new DiSink();
        await using ServiceProvider provider = Build(services =>
        {
            services.AddSingleton(sink);
            services.AddScoped<ScopedCounter>();
        });
        Executor executor = ExecutorFor(provider);
        DiscoveredHandler handler = Scan<RecordsScopeHandler>();

        await executor.InvokeAsync(MakeEnvelope(new RecordsScopeMessage()), handler);
        await executor.InvokeAsync(MakeEnvelope(new RecordsScopeMessage()), handler);

        Assert.Equal(2, sink.Seen.Count);
        Assert.NotEqual(sink.Seen[0], sink.Seen[1]);
    }

    [Fact]
    public async Task services_injected_into_one_handler_share_the_message_scope()
    {
        var sink = new DiSink();
        await using ServiceProvider provider = Build(services =>
        {
            services.AddSingleton(sink);
            services.AddScoped<ScopedCounter>();
            services.AddScoped<ScopedA>();
            services.AddScoped<ScopedB>();
        });
        Executor executor = ExecutorFor(provider);

        await executor.InvokeAsync(MakeEnvelope(new SharesScopeMessage()), Scan<SharesScopeHandler>());

        Assert.Equal([true, true], sink.Flags);
    }

    [Fact]
    public async Task scope_is_disposed_after_success()
    {
        var sink = new DiSink();
        await using ServiceProvider provider = Build(services =>
        {
            services.AddSingleton(sink);
            services.AddScoped<DisposableProbe>();
        });
        Executor executor = ExecutorFor(provider);

        await executor.InvokeAsync(MakeEnvelope(new ProbeMessage(Throws: false)), Scan<ProbeHandler>());

        Assert.Equal([true], sink.Flags);
    }

    [Fact]
    public async Task scope_is_disposed_after_handler_throws()
    {
        var sink = new DiSink();
        await using ServiceProvider provider = Build(services =>
        {
            services.AddSingleton(sink);
            services.AddScoped<DisposableProbe>();
        });
        Executor executor = ExecutorFor(provider);

        await Assert.ThrowsAsync<HandlerFault>(() =>
            executor.InvokeAsync(MakeEnvelope(new ProbeMessage(Throws: true)), Scan<ProbeHandler>()));

        Assert.Equal([true], sink.Flags);
    }

    [Fact]
    public async Task parameterless_handler_works_without_registration()
    {
        var executor = new Executor(new ErrorPolicyCatalog());

        object? result = await executor.InvokeAsync(
            MakeEnvelope(new PlainMessage("hello")),
            Scan<PlainHandler>());

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task registered_handler_is_taken_from_the_message_scope()
    {
        // Not constructible by ActivatorUtilities (string is not a service): only the registration can build it.
        await using ServiceProvider provider = Build(services =>
            services.AddScoped(_ => new RegisteredOnlyHandler("from-container")));
        Executor executor = ExecutorFor(provider);

        object? result = await executor.InvokeAsync(
            MakeEnvelope(new RegisteredOnlyMessage()),
            Scan<RegisteredOnlyHandler>());

        Assert.Equal("from-container", result);
    }

    [Fact]
    public async Task method_parameter_service_is_resolved_from_the_scope()
    {
        await using ServiceProvider provider = Build(services =>
            services.AddScoped<IGreeter>(_ => new Greeter("hi from the container")));
        Executor executor = ExecutorFor(provider);

        object? result = await executor.InvokeAsync(
            MakeEnvelope(new GreetMessage()),
            Scan<StaticGreetHandler>());

        Assert.Equal("hi from the container", result);
    }

    [Fact]
    public async Task missing_constructor_service_names_handler_and_type()
    {
        await using ServiceProvider provider = Build(_ => { });
        Executor executor = ExecutorFor(provider);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(() =>
            executor.InvokeAsync(MakeEnvelope(new NeedsCtorServiceMessage()), Scan<NeedsCtorServiceHandler>()));

        HandlerDependencyMissing missing = Assert.IsType<HandlerDependencyMissing>(fault.InnerException);
        Assert.Equal(typeof(NeedsCtorServiceHandler), missing.HandlerType);
        Assert.Contains(typeof(NeedsCtorServiceHandler).FullName!, missing.Message);
        Assert.Contains(nameof(IUnregisteredService), missing.Message);
    }

    [Fact]
    public async Task missing_method_parameter_service_names_handler_and_type()
    {
        await using ServiceProvider provider = Build(_ => { });
        Executor executor = ExecutorFor(provider);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(() =>
            executor.InvokeAsync(MakeEnvelope(new NeedsMethodServiceMessage()), Scan<NeedsMethodServiceHandler>()));

        HandlerDependencyMissing missing = Assert.IsType<HandlerDependencyMissing>(fault.InnerException);
        Assert.Equal(typeof(NeedsMethodServiceHandler), missing.HandlerType);
        Assert.Equal(typeof(IUnregisteredService), missing.ServiceType);
        Assert.Contains(typeof(NeedsMethodServiceHandler).FullName!, missing.Message);
        Assert.Contains(typeof(IUnregisteredService).FullName!, missing.Message);
        Assert.Contains("'service'", missing.Message);
    }

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Executor ExecutorFor(ServiceProvider provider) =>
        new(new ErrorPolicyCatalog(), scopes: provider.GetRequiredService<IServiceScopeFactory>());

    private static DiscoveredHandler Scan<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }

    private static Envelope MakeEnvelope(object message) =>
        EnvelopeFactory.Create(
            message: new Message(message),
            messageType: MessageTypeNaming.For(message.GetType()));
}

public sealed class DiSink
{
    public List<Guid> Seen { get; } = [];

    public List<bool> Flags { get; } = [];
}

public sealed class ScopedCounter
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class ScopedA(ScopedCounter counter)
{
    public ScopedCounter Counter { get; } = counter;
}

public sealed class ScopedB(ScopedCounter counter)
{
    public ScopedCounter Counter { get; } = counter;
}

public sealed class DisposableProbe(DiSink sink) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        sink.Flags.Add(true);
        return ValueTask.CompletedTask;
    }
}

public interface IGreeter
{
    string Greet();
}

public sealed class Greeter(string greeting) : IGreeter
{
    public string Greet() => greeting;
}

public interface IUnregisteredService;

public sealed record RecordsScopeMessage;

public sealed class RecordsScopeHandler(DiSink sink, ScopedCounter counter)
{
    public void Handle(RecordsScopeMessage message) => sink.Seen.Add(counter.Id);
}

public sealed record SharesScopeMessage;

public sealed class SharesScopeHandler(DiSink sink, ScopedCounter ctorCounter, ScopedA a, ScopedB b)
{
    public void Handle(SharesScopeMessage message, ScopedCounter methodCounter)
    {
        sink.Flags.Add(ReferenceEquals(ctorCounter, methodCounter));
        sink.Flags.Add(ReferenceEquals(a.Counter, b.Counter));
    }
}

public sealed record ProbeMessage(bool Throws);

public sealed class ProbeHandler(DisposableProbe probe)
{
    public void Handle(ProbeMessage message)
    {
        _ = probe;
        if (message.Throws)
        {
            throw new InvalidOperationException("boom");
        }
    }
}

public sealed record PlainMessage(string Text);

public sealed class PlainHandler
{
    public string Handle(PlainMessage message) => message.Text;
}

public sealed record GreetMessage;

public sealed class StaticGreetHandler
{
    public static string Handle(GreetMessage message, IGreeter greeter, CancellationToken cancellationToken) =>
        greeter.Greet();
}

public sealed record NeedsCtorServiceMessage;

public sealed class NeedsCtorServiceHandler(IUnregisteredService service)
{
    public void Handle(NeedsCtorServiceMessage message) => _ = service;
}

public sealed record NeedsMethodServiceMessage;

public sealed class NeedsMethodServiceHandler
{
    public void Handle(NeedsMethodServiceMessage message, IUnregisteredService service) => _ = service;
}

public sealed record RegisteredOnlyMessage;

public sealed class RegisteredOnlyHandler(string origin)
{
    public string Handle(RegisteredOnlyMessage message) => origin;
}
