using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.ChatEventProducer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring message broker
/// integration in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of message broker infrastructure services. This approach keeps
/// the Program.cs clean and provides a single, discoverable location for
/// message broker configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> This extension lives in the Infrastructure layer
/// and registers infrastructure implementations of the ports defined in the
/// Application layer (e.g., <c>IChatEventProducerService</c>).
/// </para>
/// <para>
/// <b>Message Broker Integration:</b> The message broker is used for streaming
/// chat events through the Chatify system. It provides:
/// <list type="bullet">
/// <item>Ordered message delivery within partitions (for chat message ordering)</item>
/// <item>Horizontal scalability through partitioning</item>
/// <item>Durable message storage with retention policies</item>
/// <item>Fan-out delivery through consumer groups</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddKafka(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:Kafka"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Kafka": {
///       "BootstrapServers": "localhost:9092",
///       "TopicName": "chat-events",
///       "Partitions": 3,
///       "BroadcastConsumerGroupPrefix": "chatify-broadcast"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// </remarks>
public static class ServiceCollectionKafkaExtensions
{
    /// <summary>
    /// Registers Chatify message broker infrastructure services with the dependency
    /// injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing message broker settings.
    /// Must not be null.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that multiple
    /// calls can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when message broker configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="KafkaOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item>Producer implementation of <see cref="IChatEventProducerService"/> (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:Kafka"</c> section to <see cref="KafkaOptionsEntity"/> and
    /// validates all required fields before registration.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed:
    /// <list type="bullet">
    /// <item><see cref="KafkaOptionsEntity.BootstrapServers"/> must not be empty</item>
    /// <item><see cref="KafkaOptionsEntity.TopicName"/> must not be empty</item>
    /// <item><see cref="KafkaOptionsEntity.Partitions"/> must be greater than zero</item>
    /// <item><see cref="KafkaOptionsEntity.BroadcastConsumerGroupPrefix"/> must not be empty</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Options: Singleton (configuration is read-only)</item>
    /// <item>Producer service: Singleton (producers are thread-safe and expensive to create)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Current Implementation:</b> This method currently performs configuration
    /// binding and validation, and registers placeholder service implementations.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddKafka(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        var kafkaSection = configuration.GetSection("Chatify:Kafka");
        var kafkaOptions = kafkaSection.Get<KafkaOptionsEntity>()
            ?? new KafkaOptionsEntity();

        if (!kafkaOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid message broker configuration. " +
                $"Please check the 'Chatify:Kafka' configuration section. " +
                $"Required fields: BootstrapServers, TopicName. " +
                $"Provided options: {kafkaOptions}",
                nameof(configuration));
        }

        services.AddSingleton(kafkaOptions);
        services.AddSingleton<IChatEventProducerService, ChatEventProducerService>();

        return services;
    }
}
