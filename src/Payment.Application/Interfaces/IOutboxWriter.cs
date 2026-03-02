namespace Payment.Application.Interfaces;

public interface IOutboxWriter
{
    Task WriteAsync<T>(string eventType, T payload, CancellationToken ct = default) where T : notnull;
}
