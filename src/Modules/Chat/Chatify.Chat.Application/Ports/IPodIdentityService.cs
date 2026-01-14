namespace Chatify.Chat.Application.Ports;

/// <summary>
/// Defines a contract for providing pod identity information within the
/// Chatify system. This service identifies which pod instance is handling
/// a request, supporting debugging, audit trails, and distributed tracing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Port Role:</b> This is a secondary adapter port in Clean Architecture terms.
/// The application layer depends on this abstraction, while the infrastructure
/// layer provides concrete implementations that extract pod identity from
/// the runtime environment.
/// </para>
/// <para>
/// <b>Purpose:</b> Pod identity enables:
/// <list type="bullet">
/// <item>Correlating logs and events from specific instances</item>
/// <item>Debugging distributed issues by tracing message flow</item>
/// <item>Audit trails showing which pod handled each operation</item>
/// <item>Implementing pod-leader election patterns</item>
/// </list>
/// </para>
/// <para>
/// <b>Environment Sources:</b> Implementations typically obtain pod identity from:
/// <list type="bullet">
/// <item>Environment variables (e.g., POD_NAME, HOSTNAME)</item>
/// <item>Kubernetes Downward API</item>
/// <item>Configuration files</item>
/// <item>Cloud provider metadata services</item>
/// </list>
/// </para>
/// <para>
/// <b>Fallback Behavior:</b> In local development environments where pod
/// concepts don't apply, implementations should return a meaningful fallback
/// value such as the machine name, "localhost", or "development".
/// </para>
/// </remarks>
public interface IPodIdentityService
{
    /// <summary>
    /// Gets the unique identifier for the current pod instance.
    /// </summary>
    /// <value>
    /// A string uniquely identifying the current pod or deployment unit.
    /// The value should be stable for the lifetime of the pod instance
    /// and unique within the cluster/deployment.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Kubernetes Format:</b> In Kubernetes deployments, this typically
    /// follows the pattern: "{deployment-name}-{pod-hash}-{random-suffix}"
    /// For example: "chat-api-7d9f4c5b6d-abc12"
    /// </para>
    /// <para>
    /// <b>Length Constraints:</b> The returned value should be within
    /// reasonable length limits (typically 256 characters) to accommodate
    /// storage in database fields and logging systems.
    /// </para>
    /// <para>
    /// <b>Character Set:</b> Pod identifiers typically use lowercase letters,
    /// numbers, and hyphens to comply with DNS naming standards when used
    /// in hostnames.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// var podId = _podIdentityService.PodId;
    /// var message = new ChatEventDto
    /// {
    ///     MessageId = Guid.NewGuid(),
    ///     // ... other properties ...
    ///     OriginPodId = podId
    /// };
    /// ]]></code>
    /// </para>
    /// </remarks>
    string PodId { get; }
}
