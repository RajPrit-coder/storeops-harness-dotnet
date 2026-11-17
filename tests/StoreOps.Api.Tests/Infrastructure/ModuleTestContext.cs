using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Alerts;
using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Reports;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Tests.Infrastructure;

/// <summary>
/// Real services over fresh in-memory repositories, with a recording event bus and a manual clock, so
/// service tests can assert business outcomes and the events a change publishes.
/// </summary>
internal sealed class ModuleTestContext
{
    public ModuleTestContext()
    {
        Staff = new StaffService(new InMemoryStaffRepository(), Time);
        Programmes = new ProgrammeService(new InMemoryProgrammeRepository(), Staff, Events, Time);
        Activities = new ActivityService(new InMemoryActivityRepository(), Staff, Programmes, Events, Time);
        Notifications = new NotificationService(new InMemoryNotificationRepository(), Staff, Events, Time);
        Reports = new ReportService(new InMemoryReportRepository(), Activities, Time);
    }

    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

    public RecordingEventBus Events { get; } = new();

    public StaffService Staff { get; }

    public ProgrammeService Programmes { get; }

    public ActivityService Activities { get; }

    public NotificationService Notifications { get; }

    public ReportService Reports { get; }
}

internal sealed class RecordingEventBus : IEventBus
{
    private readonly List<IIntegrationEvent> _published = [];

    public IReadOnlyList<IIntegrationEvent> Published => _published;

    public IEnumerable<TEvent> Of<TEvent>()
        where TEvent : IIntegrationEvent
        => _published.OfType<TEvent>();

    public void Clear() => _published.Clear();

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        _published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
