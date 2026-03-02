using LicenseCore.Application.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Commands;

namespace LicenseCore.Application.Consumers;

/// <summary>
/// Handles SendNotificationCommand. Delegates delivery to INotificationService.
/// Retry and fault-discard policies are defined in SendNotificationConsumerDefinition.
/// </summary>
public class SendNotificationConsumer : IConsumer<SendNotificationCommand>
{
    private readonly INotificationService _notifications;
    private readonly ILogger<SendNotificationConsumer> _logger;

    public SendNotificationConsumer(
        INotificationService notifications,
        ILogger<SendNotificationConsumer> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendNotificationCommand> context)
    {
        var cmd = context.Message;
        var ct = context.CancellationToken;

        try
        {
            await _notifications.SendAsync(cmd.LicenseId, cmd.Message, channel: "default", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send notification to license {LicenseId}: {Message}",
                cmd.LicenseId, cmd.Message);
            throw;
        }
    }
}

/// <summary>
/// Configures exponential retry + DiscardFaultedMessages so exhausted notifications
/// are silently dropped rather than polluting the global error queue.
/// </summary>
public class SendNotificationConsumerDefinition : ConsumerDefinition<SendNotificationConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SendNotificationConsumer> consumerConfigurator)
    {
        endpointConfigurator.DiscardFaultedMessages();
        consumerConfigurator.UseMessageRetry(r =>
            r.Exponential(10, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(10)));
    }
}
