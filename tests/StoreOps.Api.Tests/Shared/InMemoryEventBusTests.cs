using Microsoft.Extensions.DependencyInjection;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Tests.Shared;

public sealed class InMemoryEventBusTests
{
    [Fact]
    public async Task Publish_DeliversToEverySubscriber()
    {
        var log = new List<string>();
        await using var provider = BuildProvider(log, failFirst: false);

        await provider.GetRequiredService<IEventBus>().PublishAsync(new TestEvent("hello", DateTimeOffset.UnixEpoch));

        Assert.Equal(["first:hello", "second:hello"], log);
    }

    [Fact]
    public async Task Publish_WhenASubscriberFails_StillDeliversToOthersAndDoesNotThrow()
    {
        var log = new List<string>();
        await using var provider = BuildProvider(log, failFirst: true);

        await provider.GetRequiredService<IEventBus>().PublishAsync(new TestEvent("hello", DateTimeOffset.UnixEpoch));

        Assert.Equal(["second:hello"], log);
    }

    [Fact]
    public async Task Publish_WithNoSubscribers_IsANoOp()
    {
        await using var provider = new ServiceCollection().AddLogging().AddInMemoryEventBus().BuildServiceProvider();

        var exception = await Record.ExceptionAsync(
            () => provider.GetRequiredService<IEventBus>().PublishAsync(new TestEvent("ignored", DateTimeOffset.UnixEpoch)));

        Assert.Null(exception);
    }

    private static ServiceProvider BuildProvider(List<string> log, bool failFirst)
    {
        var services = new ServiceCollection().AddLogging().AddInMemoryEventBus();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new RecordingSubscriber("first", log, failFirst));
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new RecordingSubscriber("second", log, fail: false));
        return services.BuildServiceProvider(validateScopes: true);
    }

    internal sealed record TestEvent(string Payload, DateTimeOffset OccurredAt) : IIntegrationEvent;

    private sealed class RecordingSubscriber(string name, List<string> log, bool fail) : IEventSubscriber<TestEvent>
    {
        public Task HandleAsync(TestEvent integrationEvent, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new InvalidOperationException("subscriber failure");
            }

            log.Add($"{name}:{integrationEvent.Payload}");
            return Task.CompletedTask;
        }
    }
}
