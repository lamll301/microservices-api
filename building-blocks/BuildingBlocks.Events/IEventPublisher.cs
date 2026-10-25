namespace BuildingBlocks.Events;

/// <summary>Cổng phát integration event ra message broker. Việc chọn broker nằm ở lớp triển khai của từng service.</summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class;
}
