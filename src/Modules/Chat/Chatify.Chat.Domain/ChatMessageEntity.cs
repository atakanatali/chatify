namespace Chatify.Chat.Domain;

/// <summary>
/// Represents a single chat message entity within the Chatify domain model.
/// Messages are the core unit of communication and are strictly ordered within
/// their scope to maintain conversation integrity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering Guarantees:</b> Messages are ordered strictly by CreatedAtUtc
/// timestamp within each unique scope (ScopeType + ScopeId combination). This
/// ensures that all participants see messages in the same chronological sequence,
/// which is essential for maintaining conversation context and coherence.
/// </para>
/// <para>
/// <b>Scope Identity:</b> The combination of ScopeType and ScopeId uniquely
/// identifies a conversation context. Messages with different scope identifiers
/// are ordered independently, allowing parallel processing across channels and
/// direct messages without blocking.
/// </para>
/// <para>
/// <b>Origin Tracking:</b> The OriginPodId property identifies which pod
/// originally created the message, supporting debugging, audit trails, and
/// distributed tracing in Kubernetes deployments.
/// </para>
/// <para>
/// <b>Immutability:</b> Once created, a ChatMessageEntity should be considered
/// immutable. Message edits, if supported, should be implemented as new
/// entities with references to the original, rather than in-place modifications.
/// </para>
/// </remarks>
public record ChatMessageEntity
{
    /// <summary>
    /// Gets the unique identifier for this message.
    /// </summary>
    /// <value>
    /// A GUID that uniquely identifies this message across the entire Chatify system.
    /// This identifier is generated at message creation time and never reused.
    /// </value>
    /// <remarks>
    /// The MessageId is used for message deduplication, idempotent operations,
    /// and as the primary key in persistence layers. It enables reliable message
    /// delivery even in the presence of network failures or retries.
    /// </remarks>
    public required Guid MessageId { get; init; }

    /// <summary>
    /// Gets the type of chat scope this message belongs to.
    /// </summary>
    /// <value>
    /// A <see cref="ChatScopeTypeEnum"/> value indicating whether this message
    /// belongs to a Channel or DirectMessage scope.
    /// </value>
    /// <remarks>
    /// The scope type determines how the message is routed, which participants
    /// can access it, and how ordering is applied. Messages in different scope
    /// types are ordered independently even if they share the same ScopeId.
    /// </remarks>
    public required ChatScopeTypeEnum ScopeType { get; init; }

    /// <summary>
    /// Gets the scope identifier that groups this message with related messages.
    /// </summary>
    /// <value>
    /// A string identifying the specific scope within the scope type.
    /// For channels, this may be a channel name or UUID.
    /// For direct messages, this may be a composite key derived from participant IDs.
    /// </value>
    /// <remarks>
    /// <para>
    /// The ScopeId, combined with ScopeType, defines the complete ordering context
    /// for a message. All messages sharing the same ScopeType and ScopeId are
    /// processed in strict chronological order.
    /// </para>
    /// <para>
    /// ScopeId values must be validated by <see cref="ChatDomainPolicy.ValidateScopeId"/>
    /// before being used to create a message. The policy enforces length constraints
    /// and format requirements to ensure consistent processing.
    /// </para>
    /// </remarks>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the identifier of the user who sent this message.
    /// </summary>
    /// <value>
    /// A string uniquely identifying the sender within the Chatify system.
    /// This typically corresponds to a user ID from the identity provider.
    /// </value>
    /// <remarks>
    /// <para>
    /// The SenderId is used for authorization (ensuring only the sender can
    /// delete their messages), display purposes (showing who sent each message),
    /// and access control (determining which scopes a user can participate in).
    /// </para>
    /// <para>
    /// SenderId values must be validated by <see cref="ChatDomainPolicy.ValidateSenderId"/>
    /// before being used to create a message.
    /// </para>
    /// </remarks>
    public required string SenderId { get; init; }

    /// <summary>
    /// Gets the actual text content of the message.
    /// </summary>
    /// <value>
    /// The message text as provided by the sender. This may include Unicode
    /// characters, emoji, and other text content.
    /// </value>
    /// <remarks>
    /// <para>
    /// Message text is subject to length constraints enforced by
    /// <see cref="ChatDomainPolicy.ValidateText"/>. The maximum length is defined
    /// by <see cref="ChatDomainPolicy.MaxTextLength"/> to prevent abuse and
    /// ensure consistent performance.
    /// </para>
    /// <para>
    /// The text is stored as-is without sanitization. Any HTML/Markdown rendering
    /// or XSS protection should be applied at the presentation layer, not when
    /// storing the message.
    /// </para>
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when this message was created.
    /// </summary>
    /// <value>
    /// A <see cref="DateTime"/> value in UTC representing when the message
    /// was originally created. This value is set at message creation time
    /// and never modified.
    /// </value>
    /// <remarks>
    /// <para>
    /// The CreatedAtUtc timestamp is the primary ordering key for messages
    /// within a scope. Messages are processed in strictly increasing order
    /// of CreatedAtUtc values to ensure all participants see the same sequence.
    /// </para>
    /// <para>
    /// This timestamp should use high-precision time (including ticks or
    /// microseconds) to handle cases where multiple messages are created
    /// in rapid succession. In cases of identical timestamps, the MessageId
    /// serves as a tiebreaker for total ordering.
    /// </para>
    /// </remarks>
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the identifier of the pod that originally created this message.
    /// </summary>
    /// <value>
    /// A string identifying the Kubernetes pod (or equivalent deployment unit)
    /// that handled the initial message creation request.
    /// </value>
    /// <remarks>
    /// <para>
    /// The OriginPodId supports operational concerns including debugging,
    /// distributed tracing, and audit logging. When investigating issues,
    /// knowing which pod handled a request helps correlate message events
    /// with logs and metrics from specific instances.
    /// </para>
    /// <para>
    /// In Kubernetes deployments, this typically contains the pod name
    /// (e.g., "chat-api-7d9f4c5b6d-abc12"). In development environments,
    /// it may contain a machine name or other identifier.
    /// </para>
    /// <para>
    /// This field is populated from infrastructure context at message creation
    /// time and should not be provided by end users.
    /// </para>
    /// </remarks>
    public required string OriginPodId { get; init; }
}
