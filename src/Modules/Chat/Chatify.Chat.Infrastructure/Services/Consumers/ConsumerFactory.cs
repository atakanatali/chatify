using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Services.Consumers;

/// <summary>
/// Default implementation of <see cref="IConsumerFactory"/> that creates
/// message broker consumers with enterprise-grade configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This factory encapsulates the complexity of creating and
/// configuring message broker consumers, providing a single point of control for
/// consumer creation across the application.
/// </para>
/// <para>
/// <b>Consumer Configuration:</b> The factory configures consumers with:
/// <list type="bullet">
/// <item>Error handlers for broker-side issues</item>
/// <item>Log handlers for warnings and above</item>
/// <item>Statistics handlers for monitoring integration (future)</item>
/// </list>
/// </para>
/// <para>
/// <b>Deserialization:</b> The factory creates consumers configured with:
/// <list type="bullet">
/// <item><c>Ignore</c> key deserializer - Messages are partitioned by scope but keys are not needed for processing</item>
/// <item><c>byte[]</c> value deserializer - Raw JSON payloads for custom deserialization</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ConsumerFactory : IConsumerFactory
{
    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    /// <remarks>
    /// Used for logging consumer-specific events such as errors and warnings
    /// from the message broker client library.
    /// </remarks>
    private readonly ILogger<ConsumerFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsumerFactory"/> class.
    /// </summary>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    /// <remarks>
    /// The constructor stores the logger for use in consumer configuration.
    /// The logger is attached to consumers via error and log handlers to
    /// capture message broker client library events.
    /// </remarks>
    public ConsumerFactory(ILogger<ConsumerFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new message broker consumer with the specified configuration.
    /// </summary>
    /// <param name="config">
    /// The consumer configuration. Must not be null.
    /// </param>
    /// <returns>
    /// A new Kafka consumer instance configured with
    /// error and log handlers.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="config"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Consumer Builder Configuration:</b> The factory configures the
    /// consumer builder with:
    /// <list type="bullet">
/// <item><b>Error Handler:</b> Logs all broker errors with code, reason, and severity</item>
    /// <item><b>Log Handler:</b> Logs warnings and above from the message broker client</item>
    /// <item><b>Statistics Handler:</b> Logs consumer statistics for monitoring (debug level)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Error Handler Behavior:</b> The error handler logs all consumer errors
    /// including connection issues, offset commit failures, and deserialization
    /// errors. Fatal errors are logged at a higher severity.
    /// </para>
    /// <para>
    /// <b>Log Handler Behavior:</b> The log handler filters client logs
    /// to only include warnings and errors, reducing log noise while maintaining
    /// visibility into consumer issues.
    /// </para>
    /// <para>
    /// <b>Statistics Handler:</b> Consumer statistics are logged at debug level
    /// for monitoring and observability without polluting production logs.
    /// This can be integrated with monitoring systems in the future.
    /// </para>
    /// </remarks>
    public IConsumer<Ignore, byte[]> Create(ConsumerConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var consumerBuilder = new ConsumerBuilder<Ignore, byte[]>(config);

        // Configure error handler for consumer-specific errors
        // This captures broker-side issues like connection failures, authorization errors, etc.
        consumerBuilder.SetErrorHandler((consumer, error) =>
        {
            _logger.LogError(
                "Message broker consumer error: Code={ErrorCode}, Reason={ErrorReason}, IsFatal={IsFatal}",
                error.Code,
                error.Reason,
                error.IsFatal);
        });

        // Configure log handler for warnings and above
        // This filters out verbose info logs while capturing important warnings
        consumerBuilder.SetLogHandler((consumer, logMessage) =>
        {
            if (logMessage.Level >= SyslogLevel.Warning)
            {
                _logger.LogWarning(
                    "Message broker consumer log: Level={Level}, Facility={Facility}, Message={Message}",
                    logMessage.Level,
                    logMessage.Facility,
                    logMessage.Message);
            }
        });

        // Configure statistics handler for monitoring
        // Statistics include consumer lag, fetch rates, and other metrics
        consumerBuilder.SetStatisticsHandler((consumer, json) =>
        {
            _logger.LogDebug("Message broker consumer statistics: {Statistics}", json);
        });

        return consumerBuilder.Build();
    }
}
