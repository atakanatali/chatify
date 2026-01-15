using Confluent.Kafka;

namespace Chatify.Chat.Infrastructure.Services.Consumers;

/// <summary>
/// Defines a factory abstraction for creating message broker consumers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This interface abstracts the creation of message broker consumers,
/// allowing the background service to be decoupled from direct consumer instantiation.
/// This improves testability by enabling mock implementations and isolates
/// consumer configuration logic.
/// </para>
/// <para>
/// <b>Design Rationale:</b> By depending on a factory rather than creating
/// consumers directly, the background service follows the Dependency Inversion
/// Principle (DIP). The factory handles the complexity of consumer builder
/// configuration, while the service focuses on message consumption logic.
/// </para>
/// <para>
/// <b>Usage Pattern:</b> The factory is called during background service
/// initialization to create a consumer with the specified configuration.
/// The consumer is disposed when the service shuts down.
/// </para>
/// </remarks>
public interface IConsumerFactory
{
    /// <summary>
    /// Creates a new message broker consumer with the specified configuration.
    /// </summary>
    /// <param name="config">
    /// The consumer configuration including bootstrap servers, group ID,
    /// deserializers, and other settings. Must not be null.
    /// </param>
    /// <returns>
    /// A new <see cref="IConsumer{TKey, TValue}"/> instance ready for use.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Lifetime Management:</b> The returned consumer implements <see cref="IDisposable"/>
    /// and should be properly disposed when no longer needed. The calling code
    /// is responsible for the consumer's lifecycle.
    /// </para>
    /// <para>
    /// <b>Thread Safety:</b> The returned consumer is NOT thread-safe. Each consumer
    /// instance should be used from a single thread. Multiple consumers require
    /// separate instances from this factory.
    /// </para>
    /// <para>
    /// <b>Configuration:</b> The configuration must include appropriate key and
    /// value deserializers. For the chat history writer, the consumer uses
    /// <c>Ignore</c> for keys (messages are partitioned by scope but key is
    /// not needed for processing) and <c>byte[]</c> for values (JSON payloads).
    /// </para>
    /// </remarks>
    IConsumer<Ignore, byte[]> Create(ConsumerConfig config);
}
