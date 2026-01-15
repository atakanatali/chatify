using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.ChatHistory.ChatEventProcessing;
using Chatify.Chat.Infrastructure.Services.Consumers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring chat history writer infrastructure
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate the
/// registration of chat history writer services for consuming events from Kafka
/// and persisting them to ScyllaDB. This approach keeps the Program.cs clean and
/// provides a single, discoverable location for history writer configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> This extension lives in the Infrastructure layer
/// and registers infrastructure implementations that support the background service
/// in the API host layer.
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddChatHistoryWriter(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:ChatHistoryWriter"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "ChatHistoryWriter": {
///       "ConsumerGroupId": "chatify-chat-history-writer",
///       "DatabaseRetryMaxAttempts": 5,
///       "DatabaseRetryBaseDelayMs": 100,
///       "DatabaseRetryMaxDelayMs": 10000
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Service Lifetimes:</b> All services are registered as singletons because:
/// <list type="bullet">
/// <item>Options are read-only after startup</item>
/// <item>Factory is thread-safe and lightweight</item>
/// <item>Processor maintains state (Polly policy) that should be shared</item>
/// </list>
/// </para>
/// </remarks>
public static class ServiceCollectionChatHistoryWriterExtensions
{
    /// <summary>
    /// Registers Chatify chat history writer infrastructure services with the dependency
    /// injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing chat history writer settings.
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
    /// Thrown when chat history writer configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="ChatHistoryWriterOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item><see cref="ConsumerFactory"/> as implementation of <see cref="IConsumerFactory"/> (singleton)</item>
    /// <item><see cref="ChatEventProcessor"/> as implementation of <see cref="IChatEventProcessor"/> (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:ChatHistoryWriter"</c> section to <see cref="ChatHistoryWriterOptionsEntity"/>
    /// and validates all required fields before registration. This fails fast
    /// during startup if configuration is invalid.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed via
    /// <see cref="ChatHistoryWriterOptionsEntity.IsValid()"/>:
    /// <list type="bullet">
    /// <item><see cref="ChatHistoryWriterOptionsEntity.ConsumerGroupId"/> must not be empty</item>
    /// <item><see cref="ChatHistoryWriterOptionsEntity.ClientIdPrefix"/> must not be empty</item>
    /// <item><see cref="ChatHistoryWriterOptionsEntity.DatabaseRetryMaxAttempts"/> must be greater than zero</item>
    /// <item><see cref="ChatHistoryWriterOptionsEntity.DatabaseRetryBaseDelayMs"/> must be greater than zero</item>
    /// <item><see cref="ChatHistoryWriterOptionsEntity.DatabaseRetryMaxDelayMs"/> must be greater than base delay</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Options: Singleton (configuration is read-only after startup)</item>
    /// <item>Factory: Singleton (stateless, thread-safe)</item>
    /// <item>Processor: Singleton (maintains Polly retry policy)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Registration Order:</b> This extension should be called after
    /// infrastructure providers registration but before application services
    /// registration. The recommended order in Program.cs is:
    /// <code><![CDATA[
    /// // 1. Infrastructure Options (must be first)
    /// builder.Services.AddElasticLoggingChatify(Configuration);
    ///
    /// // 2. Infrastructure Providers
    /// builder.Services.AddDatabase(Configuration);
    /// builder.Services.AddCaching(Configuration);
    /// builder.Services.AddMessageBroker(Configuration);
    /// builder.Services.AddChatHistoryWriter(Configuration);  // <-- This extension
    ///
    /// // 3. Application Services
    /// builder.Services.AddChatifyChatApplication();
    /// ]]></code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddChatHistoryWriter(
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

        // Bind configuration from the "Chatify:ChatHistoryWriter" section
        var writerSection = configuration.GetSection("Chatify:ChatHistoryWriter");
        var writerOptions = writerSection.Get<ChatHistoryWriterOptionsEntity>()
            ?? new ChatHistoryWriterOptionsEntity();

        // Validate the configuration before registration
        if (!writerOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid chat history writer configuration. " +
                $"Please check the 'Chatify:ChatHistoryWriter' configuration section. " +
                $"Provided options: {writerOptions}",
                nameof(configuration));
        }

        // Register the validated options as a singleton
        services.AddSingleton(writerOptions);

        // Register the message broker consumer factory
        services.AddSingleton<IConsumerFactory, ConsumerFactory>();

        // Register the chat event processor
        services.AddSingleton<IChatEventProcessor, ChatEventProcessor>();

        return services;
    }
}
