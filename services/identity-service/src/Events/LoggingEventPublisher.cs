using BuildingBlocks.Events;

namespace IdentityService.Events;

/// <summary>
/// Triển khai tạm: chỉ ghi log. Thay bằng adapter broker thật (RabbitMQ, Kafka, ...) khi đã chọn broker;
/// các service khác vẫn tự làm mới cache theo TTL nên hệ thống không sai, chỉ chậm cập nhật.
/// </summary>
public class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
    {
        _logger.LogInformation("Event {EventType}: {@Event}", typeof(TEvent).Name, @event);
        return Task.CompletedTask;
    }
}
