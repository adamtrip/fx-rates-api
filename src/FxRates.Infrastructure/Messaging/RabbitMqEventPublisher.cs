using System.Text.Json;
using FxRates.Application.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FxRates.Infrastructure.Messaging;

/// <summary>
/// Publisher confirms wait for broker acceptance. Each publish uses its own channel because
/// channels are not safe for concurrent use. Failed publishes propagate without retry.
/// </summary>
public sealed class RabbitMqEventPublisher(IOptions<MessagingOptions> options, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private volatile IConnection? connection;

    public async Task PublishAsync(RateCreated message, CancellationToken cancellationToken)
    {
        var openConnection = await GetOpenConnectionAsync(cancellationToken);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await openConnection.CreateChannelAsync(channelOptions, cancellationToken);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.EventId.ToString(),
            Type = message.EventType,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds())
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await channel.BasicPublishAsync(options.Value.Exchange, routingKey: message.EventType, mandatory: false,
            properties, body, cancellationToken);
    }

    private async Task<IConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (connection is { IsOpen: true })
        {
            return connection;
        }
        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (connection is { IsOpen: true })
            {
                return connection;
            }
            if (connection is not null)
            {
                // Replace connections that stayed closed despite automatic recovery.
                await connection.DisposeAsync();
            }
            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                ClientProvidedName = "fx-rates-api"
            };
            var newConnection = await factory.CreateConnectionAsync(cancellationToken);
            try
            {
                await using (var channel = await newConnection.CreateChannelAsync(cancellationToken: cancellationToken))
                {
                    await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
                        cancellationToken: cancellationToken);
                    await channel.QueueDeclareAsync(settings.Queue, durable: true, exclusive: false, autoDelete: false,
                        cancellationToken: cancellationToken);
                    await channel.QueueBindAsync(settings.Queue, settings.Exchange, "rate.created.#",
                        cancellationToken: cancellationToken);
                }
                logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}; publishing to exchange {Exchange}",
                    settings.Host, settings.Port, settings.Exchange);
                // Publish the connection only after its topology is ready for concurrent callers.
                connection = newConnection;
                return newConnection;
            }
            catch
            {
                await newConnection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
        connectionLock.Dispose();
    }
}
