using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Application.Common.Errors;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Domain;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Application.Commands.SendChatMessage;

/// <summary>
/// Handles the <see cref="SendChatMessageCommand"/> to process chat message
/// send operations. This handler orchestrates validation, rate limiting,
/// event creation, and event production following Clean Architecture principles.
/// </summary>
/// <remarks>
/// <para>
/// <b>Handler Responsibilities:</b> The command handler is responsible for
/// coordinating the flow of a message send operation, including:
/// <list type="bullet">
/// <item>Validating domain rules via <see cref="ChatDomainPolicy"/></item>
/// <item>Enforcing rate limits via <see cref="IRateLimitService"/></item>
/// <item>Creating the chat event with origin pod tracking</item>
/// <item>Producing the event to the messaging system</item>
/// <item>Returning enriched event data with delivery metadata</item>
/// </list>
/// </para>
/// <para>
/// <b>Error Handling:</b> The handler uses <see cref="ResultEntity"/> pattern
/// to return structured results rather than throwing exceptions for
/// expected failures (validation errors, rate limits). This enables
/// predictable error handling at the API layer.
/// </para>
/// <para>
/// <b>Idempotency:</b> The handler produces events with unique MessageId values,
/// enabling downstream idempotency checks. Retrying the same command will
/// produce duplicate events with the same content but different metadata.
/// </para>
/// <para>
/// <b>Performance:</b> The handler validates and rate limits before any
/// expensive operations, failing fast for invalid requests. Event production
/// is the final operation, ensuring only valid, rate-limited messages are
/// sent to the messaging system.
/// </para>
/// </remarks>
public class SendChatMessageCommandHandler
{
    private readonly IChatEventProducerService _chatEventProducerService;
    private readonly IRateLimitService _rateLimitService;
    private readonly IPodIdentityService _podIdentityService;
    private readonly ILogger<SendChatMessageCommandHandler> _logger;
    private readonly IClockService _clockService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendChatMessageCommandHandler"/> class.
    /// </summary>
    /// <param name="chatEventProducerService">
    /// The service for producing chat events to the messaging system.
    /// Must not be null.
    /// </param>
    /// <param name="rateLimitService">
    /// The service for enforcing rate limits on message sending.
    /// Must not be null.
    /// </param>
    /// <param name="podIdentityService">
    /// The service for obtaining the current pod's identity.
    /// Must not be null.
    /// </param>
    /// <param name="clockService">
    /// The service for obtaining the current time.
    /// Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger for diagnostic and audit information.
    /// Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// All dependencies are injected via constructor injection following
    /// the Dependency Inversion Principle. The handler depends only on
    /// abstractions (interfaces) defined in the Application layer, not
    /// on concrete implementations from the Infrastructure layer.
    /// </para>
    /// </remarks>
    public SendChatMessageCommandHandler(
        IChatEventProducerService chatEventProducerService,
        IRateLimitService rateLimitService,
        IPodIdentityService podIdentityService,
        IClockService clockService,
        ILogger<SendChatMessageCommandHandler> logger)
    {
        _chatEventProducerService = chatEventProducerService ?? throw new ArgumentNullException(nameof(chatEventProducerService));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _podIdentityService = podIdentityService ?? throw new ArgumentNullException(nameof(podIdentityService));
        _clockService = clockService ?? throw new ArgumentNullException(nameof(clockService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the <see cref="SendChatMessageCommand"/> by validating,
    /// rate limiting, and producing the chat event to the messaging system.
    /// </summary>
    /// <param name="command">
    /// The command containing the sender ID and message request details.
    /// Must not be null.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation,
    /// containing a <see cref="ResultEntity{T}"/> with:
    /// <list type="bullet">
    /// <item>On success: An <see cref="EnrichedChatEventDto"/> with the created
    /// event and delivery metadata (partition, offset).</item>
    /// <item>On failure: An <see cref="ErrorEntity"/> describing the failure.</item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="command"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Processing Flow:</b>
    /// <list type="number">
    /// <item>Validate all inputs against domain policy</item>
    /// <item>Check and increment rate limit counter for the sender</item>
    /// <item>Create ChatEventDto with origin pod ID</item>
    /// <item>Produce event to messaging system (e.g., Kafka)</item>
    /// <item>Return enriched event with delivery metadata</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Domain Validation:</b> All inputs are validated using
    /// <see cref="ChatDomainPolicy"/> methods before any processing occurs.
    /// This ensures invalid data is rejected early with clear error messages.
    /// </para>
    /// <para>
    /// <b>Rate Limiting:</b> A per-user rate limit is applied to prevent spam
    /// and abuse. The limit is checked before event production, ensuring
    /// rate-limited users don't consume messaging system resources.
    /// </para>
    /// <para>
    /// <b>Event Creation:</b> The ChatEventDto is created with a unique MessageId,
    /// the current UTC timestamp, and the origin pod ID for tracking.
    /// </para>
    /// <para>
    /// <b>Event Production:</b> The event is produced to the messaging system,
    /// which returns partition and offset metadata for delivery tracking.
    /// </para>
    /// </remarks>
    public async Task<ResultEntity<EnrichedChatEventDto>> HandleAsync(
        SendChatMessageCommand command,
        CancellationToken cancellationToken)
    {
        GuardUtility.NotNull(command);

        var senderId = command.SenderId;
        var request = command.Request;

        _logger.LogDebug(
            ChatifyConstants.LogMessages.ProcessingSendChatMessage,
            senderId,
            request.ScopeType,
            request.ScopeId);

        try
        {
            ChatDomainPolicy.ValidateSenderId(senderId);
            ChatDomainPolicy.ValidateScopeId(request.ScopeId);
            ChatDomainPolicy.ValidateText(request.Text);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ChatifyConstants.LogMessages.DomainValidationFailed,
                senderId,
                ex.Message);

            return ResultEntity<EnrichedChatEventDto>.Failure(ServiceError.Chat.ValidationFailed(ex.Message, ex));
        }

        var rateLimitKey = ChatifyConstants.RateLimit.SendChatMessageKey(senderId);
        var rateLimitResult = await _rateLimitService.CheckAndIncrementAsync(
            rateLimitKey,
            ChatifyConstants.RateLimit.SendChatMessageThreshold,
            ChatifyConstants.RateLimit.SendChatMessageWindowSeconds,
            cancellationToken);

        if (rateLimitResult.IsFailure)
        {
            _logger.LogWarning(
                ChatifyConstants.LogMessages.RateLimitExceeded,
                senderId);

            return ResultEntity<EnrichedChatEventDto>.Failure(ServiceError.Chat.RateLimitExceeded(senderId, rateLimitResult.Error));
        }

        var messageId = Guid.NewGuid();
        var createdAtUtc = _clockService.UtcNow;
        var originPodId = _podIdentityService.PodId;

        try
        {
            ChatDomainPolicy.ValidateOriginPodId(originPodId);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(
                ChatifyConstants.LogMessages.OriginPodIdValidationFailed,
                ex.Message);

            return ResultEntity<EnrichedChatEventDto>.Failure(ServiceError.System.ConfigurationError(ex));
        }

        var chatEvent = new ChatEventDto
        {
            MessageId = messageId,
            ScopeType = request.ScopeType,
            ScopeId = request.ScopeId,
            SenderId = senderId,
            Text = request.Text,
            CreatedAtUtc = createdAtUtc,
            OriginPodId = originPodId
        };

        try
        {
            var (partition, offset) = await _chatEventProducerService.ProduceAsync(
                chatEvent,
                cancellationToken);

            _logger.LogInformation(
                ChatifyConstants.LogMessages.SuccessfullyProducedMessage,
                messageId,
                senderId,
                partition,
                offset);

            var enrichedEvent = new EnrichedChatEventDto(
                chatEvent,
                partition,
                offset);

            return ResultEntity<EnrichedChatEventDto>.Success(enrichedEvent);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            _logger.LogError(
                ex,
                ChatifyConstants.LogMessages.FailedToProduceMessage,
                messageId,
                senderId);

            return ResultEntity<EnrichedChatEventDto>.Failure(ServiceError.Messaging.EventProductionFailed(ex));
        }
    }
}
