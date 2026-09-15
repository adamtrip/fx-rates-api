using System.Text.Json;
using FxRates.Application.Events;
using FxRates.Application.Rates;
using FxRates.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace FxRates.IntegrationTests.Messaging;

/// <summary>Exercises the publisher against a real broker. Consumes through the client library directly.</summary>
public sealed class RabbitMqEventPublisherTests : IAsyncLifetime
{
    private const string Exchange = "fx-rates.test";
    private const string Queue = "fx-rates.test.rate-created";
    private readonly RabbitMqContainer broker = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    public Task InitializeAsync() => broker.StartAsync();

    public Task DisposeAsync() => broker.DisposeAsync().AsTask();

    [Fact]
    public async Task First_publish_arrives_in_auto_declared_queue_as_persistent_json()
    {
        var options = Options.Create(new MessagingOptions
        {
            Mode = EventPublisherMode.RabbitMq,
            Host = broker.Hostname,
            Port = broker.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
            Exchange = Exchange,
            Queue = Queue
        });
        await using var publisher = new RabbitMqEventPublisher(options, NullLogger<RabbitMqEventPublisher>.Instance);
        var quotedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var rate = new Rate("USD", "EUR", 0.9m, 0.91m, RateSources.Manual, quotedAt, null);
        var message = new RateCreated(Guid.NewGuid(), rate.UpdatedAt, rate);

        await publisher.PublishAsync(message, CancellationToken.None);

        await using var connection = await new ConnectionFactory
        {
            HostName = broker.Hostname,
            Port = broker.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq"
        }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var delivery = await ReceiveAsync(channel, Queue);
        Assert.Equal("rate.created.v1", delivery.RoutingKey);
        Assert.Equal("application/json", delivery.BasicProperties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, delivery.BasicProperties.DeliveryMode);
        Assert.Equal(message.EventId.ToString(), delivery.BasicProperties.MessageId);
        Assert.Equal("rate.created.v1", delivery.BasicProperties.Type);
        using var body = JsonDocument.Parse(delivery.Body.ToArray());
        Assert.Equal(message.EventId.ToString(), body.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("rate.created.v1", body.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("USD", body.RootElement.GetProperty("rate").GetProperty("baseCurrency").GetString());
        Assert.Equal(0.91m, body.RootElement.GetProperty("rate").GetProperty("ask").GetDecimal());
    }

    [Fact]
    public async Task Concurrent_first_publishes_all_arrive_in_auto_declared_queue()
    {
        var options = Options.Create(new MessagingOptions
        {
            Mode = EventPublisherMode.RabbitMq,
            Host = broker.Hostname,
            Port = broker.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
            Exchange = Exchange,
            Queue = Queue
        });
        await using var publisher = new RabbitMqEventPublisher(options, NullLogger<RabbitMqEventPublisher>.Instance);
        var rate = new Rate("USD", "EUR", 0.9m, 0.91m, RateSources.Manual, DateTimeOffset.UnixEpoch, null);
        var messages = Enumerable.Range(0, 5)
            .Select(_ => new RateCreated(Guid.NewGuid(), DateTimeOffset.UnixEpoch, rate)).ToArray();

        await Task.WhenAll(messages.Select(message => publisher.PublishAsync(message, CancellationToken.None)));

        await using var connection = await new ConnectionFactory
        {
            HostName = broker.Hostname,
            Port = broker.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq"
        }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var received = new HashSet<Guid>();
        foreach (var message in messages)
        {
            var delivery = await ReceiveAsync(channel, Queue);
            using var body = JsonDocument.Parse(delivery.Body.ToArray());
            Assert.True(received.Add(body.RootElement.GetProperty("eventId").GetGuid()));
        }
        Assert.True(received.SetEquals(messages.Select(message => message.EventId)));
        Assert.Null(await channel.BasicGetAsync(Queue, autoAck: true));
    }

    [Fact]
    public async Task Unreachable_broker_throws_so_the_caller_can_log_it()
    {
        var options = Options.Create(new MessagingOptions
        {
            Mode = EventPublisherMode.RabbitMq,
            Host = "127.0.0.1",
            Port = 1,
            UserName = "nobody",
            Password = "nothing"
        });
        await using var publisher = new RabbitMqEventPublisher(options, NullLogger<RabbitMqEventPublisher>.Instance);
        var rate = new Rate("USD", "EUR", 1m, 2m, RateSources.Manual, DateTimeOffset.UnixEpoch, null);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            publisher.PublishAsync(new RateCreated(Guid.NewGuid(), DateTimeOffset.UnixEpoch, rate), CancellationToken.None));
    }

    private static async Task<BasicGetResult> ReceiveAsync(IChannel channel, string queue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await channel.BasicGetAsync(queue, autoAck: true) is { } result)
            {
                return result;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("No message arrived within 10 seconds.");
    }
}
