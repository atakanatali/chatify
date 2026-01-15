using Chatify.BuildingBlocks.Primitives;

namespace Chatify.Chat.Domain;

/// <summary>
/// Provides domain policy enforcement and validation rules for chat entities.
/// This static class encapsulates business invariants and validation logic
/// to ensure data integrity and enforce domain constraints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Policy Enforcement:</b> All methods in this class validate domain invariants
/// and throw <see cref="ArgumentException"/> when validation fails. This ensures
/// that invalid entities cannot be created within the domain.
/// </para>
/// <para>
/// <b>Validation Flow:</b> Call validation methods before creating or modifying
/// domain entities. If all validation passes, proceed with entity creation.
/// If any validation fails, the exception prevents the invalid operation.
/// </para>
/// <para>
/// <b>Extensibility:</b> As the chat domain evolves, additional validation methods
/// can be added to this class to enforce new business rules while maintaining
/// a centralized validation strategy.
/// </para>
/// </remarks>
public static class ChatDomainPolicy
{
    /// <summary>
    /// Defines the maximum allowable length for message text content.
    /// </summary>
    /// <remarks>
    /// This limit prevents abuse, ensures consistent performance, and maintains
    /// reasonable storage requirements. Messages exceeding this length should
    /// be rejected before creation.
    /// </remarks>
    public const int MaxTextLength = 4096;

    /// <summary>
    /// Defines the maximum allowable length for a scope identifier.
    /// </summary>
    /// <remarks>
    /// Scope identifiers are used in indexing and frequent lookups, so keeping
    /// them reasonably sized improves performance across all layers.
    /// </remarks>
    public const int MaxScopeIdLength = 256;

    /// <summary>
    /// Defines the maximum allowable length for a sender identifier.
    /// </summary>
    /// <remarks>
    /// Sender identifiers are typically user IDs from the identity provider.
    /// This limit accommodates most ID formats (UUIDs, GUIDs, integer IDs,
    /// or composite identifiers) while preventing abuse.
    /// </remarks>
    public const int MaxSenderIdLength = 256;

    /// <summary>
    /// Defines the maximum allowable length for the origin pod identifier.
    /// </summary>
    /// <remarks>
    /// Pod names in Kubernetes follow specific patterns and length limits.
    /// This value accommodates typical pod name formats with room for custom
    /// naming conventions.
    /// </remarks>
    public const int MaxOriginPodIdLength = 256;

    /// <summary>
    /// Defines the minimum allowable length for a scope identifier.
    /// </summary>
    /// <remarks>
    /// Empty scope identifiers are not valid as they would prevent proper
    /// message routing and ordering. This minimum ensures each message
    /// belongs to a properly identified scope.
    /// </remarks>
    private const int MinScopeIdLength = 1;

    /// <summary>
    /// Defines the minimum allowable length for a sender identifier.
    /// </summary>
    /// <remarks>
    /// Every message must have an identifiable sender. Empty sender IDs
    /// would prevent attribution and access control.
    /// </remarks>
    private const int MinSenderIdLength = 1;

    /// <summary>
    /// Defines the minimum allowable length for the origin pod identifier.
    /// </summary>
    /// <remarks>
    /// While pod identifiers are infrastructure-generated, we enforce a minimum
    /// length to ensure meaningful operational tracking and debugging capabilities.
    /// </remarks>
    private const int MinOriginPodIdLength = 1;

    /// <summary>
    /// Validates a scope identifier against domain policy requirements.
    /// </summary>
    /// <param name="scopeId">
    /// The scope identifier to validate. This may be a channel name, UUID,
    /// or composite identifier derived from participant IDs.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scopeId"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="scopeId"/> is empty, consists only of whitespace,
    /// or exceeds the maximum allowable length defined by <see cref="ChatDomainPolicy.MaxScopeIdLength"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Validation Rules:</b>
    /// <list type="bullet">
    /// <item>Must not be null.</item>
    /// <item>Must not be empty or whitespace-only.</item>
    /// <item>Length must be between <see cref="ChatDomainPolicy.MinScopeIdLength"/> and <see cref="ChatDomainPolicy.MaxScopeIdLength"/> characters.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Usage:</b> Call this method before creating a <see cref="ChatMessageEntity"/>
    /// or performing any operation that requires a scope identifier.
    /// </para>
    /// <example>
    /// <code>
    /// // Validates successfully
    /// ChatDomainPolicy.ValidateScopeId("general");
    /// ChatDomainPolicy.ValidateScopeId("550e8400-e29b-41d4-a716-446655440000");
    ///
    /// // Throws ArgumentException
    /// ChatDomainPolicy.ValidateScopeId("");
    /// ChatDomainPolicy.ValidateScopeId("   ");
    /// </code>
    /// </example>
    /// </remarks>
    public static void ValidateScopeId(string scopeId)
    {
        GuardUtility.NotNull(scopeId);

        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentException("Scope identifier cannot be empty or whitespace-only.", nameof(scopeId));
        }

        if (scopeId.Length > MaxScopeIdLength)
        {
            throw new ArgumentException(
                $"Scope identifier length cannot exceed {MaxScopeIdLength} characters. " +
                $"Provided length: {scopeId.Length}.",
                nameof(scopeId));
        }
    }

    /// <summary>
    /// Validates message text content against domain policy requirements.
    /// </summary>
    /// <param name="text">
    /// The message text content to validate. May include Unicode characters,
    /// emoji, and other text content.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="text"/> exceeds the maximum allowable length
    /// defined by <see cref="ChatDomainPolicy.MaxTextLength"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Validation Rules:</b>
    /// <list type="bullet">
    /// <item>Must not be null.</item>
    /// <item>Length must not exceed <see cref="ChatDomainPolicy.MaxTextLength"/> characters.</item>
    /// <item>Empty strings are permitted (for messages with only attachments, etc.).</item>
    /// <item>Whitespace-only strings are permitted.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Note:</b> This method validates length only. Content moderation,
    /// spam detection, and other content-level validations should be handled
    /// by separate services.
    /// </para>
    /// <example>
    /// <code>
    /// // Validates successfully
    /// ChatDomainPolicy.ValidateText("Hello, world!");
    /// ChatDomainPolicy.ValidateText("");
    ///
    /// // Throws ArgumentException
    /// string longText = new string('x', MaxTextLength + 1);
    /// ChatDomainPolicy.ValidateText(longText);
    /// </code>
    /// </example>
    /// </remarks>
    public static void ValidateText(string text)
    {
        GuardUtility.NotNull(text);

        if (text.Length > MaxTextLength)
        {
            throw new ArgumentException(
                $"Message text cannot exceed {MaxTextLength} characters. " +
                $"Provided length: {text.Length}.",
                nameof(text));
        }
    }

    /// <summary>
    /// Validates a sender identifier against domain policy requirements.
    /// </summary>
    /// <param name="senderId">
    /// The sender identifier to validate. This typically corresponds to a
    /// user ID from the identity provider (e.g., a UUID, integer ID, or
    /// username).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="senderId"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="senderId"/> is empty, consists only of whitespace,
    /// or exceeds the maximum allowable length defined by <see cref="ChatDomainPolicy.MaxSenderIdLength"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Validation Rules:</b>
    /// <list type="bullet">
    /// <item>Must not be null.</item>
    /// <item>Must not be empty or whitespace-only.</item>
    /// <item>Length must be between <see cref="ChatDomainPolicy.MinSenderIdLength"/> and <see cref="ChatDomainPolicy.MaxSenderIdLength"/> characters.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Security:</b> Sender identifiers are used for authorization and
    /// access control. Validating sender IDs prevents spoofing attempts and
    /// ensures proper message attribution.
    /// </para>
    /// <example>
    /// <code>
    /// // Validates successfully
    /// ChatDomainPolicy.ValidateSenderId("user-12345");
    /// ChatDomainPolicy.ValidateSenderId("550e8400-e29b-41d4-a716-446655440000");
    ///
    /// // Throws ArgumentException
    /// ChatDomainPolicy.ValidateSenderId("");
    /// ChatDomainPolicy.ValidateSenderId("   ");
    /// </code>
    /// </example>
    /// </remarks>
    public static void ValidateSenderId(string senderId)
    {
        GuardUtility.NotNull(senderId);

        if (string.IsNullOrWhiteSpace(senderId))
        {
            throw new ArgumentException("Sender identifier cannot be empty or whitespace-only.", nameof(senderId));
        }

        if (senderId.Length > MaxSenderIdLength)
        {
            throw new ArgumentException(
                $"Sender identifier cannot exceed {MaxSenderIdLength} characters. " +
                $"Provided length: {senderId.Length}.",
                nameof(senderId));
        }
    }

    /// <summary>
    /// Validates an origin pod identifier against domain policy requirements.
    /// </summary>
    /// <param name="originPodId">
    /// The origin pod identifier to validate. This typically contains the
    /// Kubernetes pod name or equivalent deployment unit identifier.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="originPodId"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="originPodId"/> is empty, consists only of whitespace,
    /// or exceeds the maximum allowable length defined by <see cref="ChatDomainPolicy.MaxOriginPodIdLength"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Validation Rules:</b>
    /// <list type="bullet">
    /// <item>Must not be null.</item>
    /// <item>Must not be empty or whitespace-only.</item>
    /// <item>Length must be between <see cref="ChatDomainPolicy.MinOriginPodIdLength"/> and <see cref="ChatDomainPolicy.MaxOriginPodIdLength"/> characters.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Operational Use:</b> The origin pod ID supports debugging, distributed
    /// tracing, and operational monitoring. Validating pod IDs ensures that
    /// correlation data is meaningful and consistent across the system.
    /// </para>
    /// <para>
    /// <b>Infrastructure Integration:</b> In Kubernetes environments, this value
    /// is typically populated from the <c>POD_NAME</c> environment variable
    /// or downward API. Ensure the pod naming strategy produces identifiers
    /// within the length constraints.
    /// </para>
    /// </remarks>
    public static void ValidateOriginPodId(string originPodId)
    {
        GuardUtility.NotNull(originPodId);

        if (string.IsNullOrWhiteSpace(originPodId))
        {
            throw new ArgumentException("Origin pod identifier cannot be empty or whitespace-only.", nameof(originPodId));
        }

        if (originPodId.Length > MaxOriginPodIdLength)
        {
            throw new ArgumentException(
                $"Origin pod identifier cannot exceed {MaxOriginPodIdLength} characters. " +
                $"Provided length: {originPodId.Length}.",
                nameof(originPodId));
        }
    }
}
