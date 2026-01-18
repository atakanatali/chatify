using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.ChatEventProducer;
using Chatify.Chat.Infrastructure.Services.InMemoryChatEventProducer;
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
/// <item>Horizontal scalability through partitioning by scope</item>
/// <item>Durable message storage with configurable retention policies</item>
/// <item>Fan-out delivery through consumer groups for broadcast scenarios</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddMessageBroker(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:MessageBroker"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "MessageBroker": {
///       "BootstrapServers": "localhost:9092",
///       "TopicName": "chat-events",
///       "Partitions": 3,
///       "BroadcastConsumerGroupPrefix": "chatify-broadcast"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Service Lifetime:</b> The producer service is registered as a singleton
/// because message broker producers are thread-safe, expensive to create, and designed
/// for long-lived reuse across requests.
/// </para>
/// </remarks>
public static class ServiceCollectionMessageBrokerExtensions
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
    /// <item><see cref="ChatEventProducerService"/> or <see cref="InMemoryChatEventProducerService"/>
    /// as implementation of <see cref="IChatEventProducerService"/> (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:MessageBroker"</c> section to <see cref="KafkaOptionsEntity"/> and
    /// validates all required fields before registration. This fails fast
    /// during startup if configuration is invalid.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed via
    /// <see cref="KafkaOptionsEntity.IsValid()"/>:
    /// <list type="bullet">
    /// <item>When <see cref="KafkaOptionsEntity.UseInMemoryBroker"/> is <c>false</c>:
    /// <list type="bullet">
    /// <item><see cref="KafkaOptionsEntity.BootstrapServers"/> must not be empty</item>
    /// <item><see cref="KafkaOptionsEntity.TopicName"/> must not be empty</item>
    /// <item><see cref="KafkaOptionsEntity.Partitions"/> must be greater than zero</item>
    /// <item><see cref="KafkaOptionsEntity.BroadcastConsumerGroupPrefix"/> must not be empty</item>
    /// </list>
    /// </item>
    /// <item>When <see cref="KafkaOptionsEntity.UseInMemoryBroker"/> is <c>true</c>:
    /// No validation of broker-specific fields is performed.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Producer Configuration:</b> The <see cref="ChatEventProducerService"/>
    /// is configured with production-grade settings:
    /// <list type="bullet">
    /// <item><c>acks=all</c>: Waits for all in-sync replicas to acknowledge</item>
    /// <item><c>enable.idempotence=true</c>: Prevents duplicates on retry</item>
    /// <item><c>retries=INT_MAX</c>: Retries indefinitely on transient failures</item>
    /// <item><c>compression.type=snappy</c>: Efficient network compression</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>In-Memory Producer:</b> When <see cref="KafkaOptionsEntity.UseInMemoryBroker"/> is <c>true</c>,
    /// <see cref="InMemoryChatEventProducerService"/> is registered instead. This service
    /// simulates message broker behavior without external dependencies.
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Options: Singleton (configuration is read-only after startup)</item>
    /// <item>Producer service: Singleton (thread-safe, expensive to create)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Registration Order:</b> This extension should be called after
    /// infrastructure options registration but before application services
    /// registration. The recommended order in Program.cs is:
    /// <code><![CDATA[
    /// // 1. Infrastructure Options (must be first)
    /// builder.Services.AddElasticLoggingChatify(Configuration);
    ///
    /// // 2. Infrastructure Providers
    /// builder.Services.AddDatabase(Configuration);
    /// builder.Services.AddCaching(Configuration);
    /// builder.Services.AddMessageBroker(Configuration);  // <-- This extension
    ///
    /// // 3. Application Services
    /// builder.Services.AddChatifyChatApplication();
    /// ]]></code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMessageBroker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Guard against null arguments
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        // Bind configuration from the "Chatify:MessageBroker" section
        var brokerSection = configuration.GetSection("Chatify:MessageBroker");
        var brokerOptions = brokerSection.Get<KafkaOptionsEntity>()
            ?? new KafkaOptionsEntity();

        // Validate the configuration before registration
        if (!brokerOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid message broker configuration. " +
                $"Please check the 'Chatify:MessageBroker' configuration section. " +
                $"Required fields: BootstrapServers, TopicName (unless UseInMemoryBroker is true). " +
                $"Provided options: {brokerOptions}",
                nameof(configuration));
        }

        // Register the validated options as a singleton
        services.AddSingleton(brokerOptions);

        // Register the event producer service based on the UseInMemoryBroker flag
        if (brokerOptions.UseInMemoryBroker)
        {
            services.AddSingleton<IChatEventProducerService, InMemoryChatEventProducerService>();
        }
        else
        {
            // Note: ChatEventProducerService implements IDisposable, which will be
            // handled by the DI container on application shutdown
            services.AddSingleton<IChatEventProducerService, ChatEventProducerService>();
        }

        return services;
    }
}
