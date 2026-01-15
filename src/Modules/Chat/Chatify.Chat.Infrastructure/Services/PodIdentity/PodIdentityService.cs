using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Ports;

namespace Chatify.Chat.Infrastructure.Services.PodIdentity;

/// <summary>
/// Provides pod identity information for the current Chatify instance.
/// This implementation extracts pod identity from environment variables
/// set by Kubernetes or uses fallback values for local development.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> The pod identity service identifies which deployment
/// unit is handling a request, enabling:
/// <list type="bullet">
/// <item>Correlating logs and events from specific instances</item>
/// <item>Debugging distributed issues by tracing message flow</item>
/// <item>Audit trails showing which pod handled each operation</item>
/// <item>Implementing pod-leader election patterns</item>
/// </list>
/// </para>
/// <para>
/// <b>Environment Variable Resolution:</b> The service reads pod identity
/// from the following environment variables in priority order:
/// <list type="number">
/// <item><c>POD_NAME</c> - Set via Kubernetes Downward API</item>
/// <item><c>HOSTNAME</c> - Automatically set in Kubernetes pods</item>
/// <item><c>COMPUTERNAME</c> - Fallback for Windows environments</item>
/// <item><c>MACHINE_NAME</c> - Fallback that may be set in some environments</item>
/// </list>
/// </para>
/// <para>
/// <b>Development Fallback:</b> If none of the above environment variables
/// are set, the service returns "localhost" to indicate a local development
/// environment. This ensures the application functions correctly during
/// development and testing.
/// </para>
/// <para>
/// <b>Kubernetes Configuration:</b> To properly configure pod identity in
/// Kubernetes, add the following to your pod specification:
/// <code><![CDATA[
/// spec:
///   containers:
///   - name: chat-api
///     env:
///     - name: POD_NAME
///       valueFrom:
///         fieldRef:
///           fieldPath: metadata.name
/// ]]></code>
/// This injects the pod's name into the POD_NAME environment variable.
/// </para>
/// <para>
/// <b>Singleton Pattern:</b> This service is registered as a singleton
/// because pod identity is a runtime property that doesn't change for
/// the lifetime of the application instance. The value is cached after
/// first access for performance.
/// </para>
/// </remarks>
public sealed class PodIdentityService : IPodIdentityService
{
    /// <summary>
    /// The cached pod identifier value, populated on first access.
    /// </summary>
    /// <remarks>
    /// This field is null until <see cref="PodId"/> is accessed for the
    /// first time. The caching strategy assumes pod identity is immutable
    /// for the lifetime of the application instance, which is always true
    /// in containerized environments.
    /// </remarks>
    private string? _cachedPodId;

    /// <summary>
    /// The object used to synchronize access to the cached pod identifier.
    /// </summary>
    /// <remarks>
    /// This lock ensures thread-safe lazy initialization of the cached
    /// pod ID value, preventing race conditions during startup when
    /// multiple requests may attempt to access the property simultaneously.
    /// </remarks>
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PodIdentityService"/> class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constructor is parameterless to support singleton registration
    /// in the DI container. Pod identity is determined from the runtime
    /// environment, not from constructor parameters.
    /// </para>
    /// <para>
    /// The pod ID value is resolved lazily on first access to <see cref="PodId"/>,
    /// rather than in the constructor, to avoid environment variable access
    /// during application startup when logging infrastructure may not be
    /// fully initialized.
    /// </para>
    /// </remarks>
    public PodIdentityService()
    {
    }

    /// <summary>
    /// Gets the unique identifier for the current pod instance.
    /// </summary>
    /// <value>
    /// A string uniquely identifying the current pod or deployment unit.
    /// The value is determined from environment variables and falls back to
    /// "localhost" in development environments.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Resolution Order:</b> The pod ID is determined by checking the
    /// following environment variables in order:
    /// <list type="number">
    /// <item><c>POD_NAME</c> - Kubernetes Downward API field reference</item>
    /// <item><c>HOSTNAME</c> - Automatically set in Kubernetes pods</item>
    /// <item><c>COMPUTERNAME</c> - Windows environment fallback</item>
    /// <item><c>MACHINE_NAME</c> - Generic environment fallback</item>
    /// <item><c>"localhost"</c> - Final fallback for development</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Caching:</b> The value is cached after first access to avoid
    /// repeated environment variable lookups. This is safe because pod
    /// identity is immutable for the lifetime of the application instance.
    /// </para>
    /// <para>
    /// <b>Thread Safety:</b> This property uses double-check locking to
    /// ensure thread-safe lazy initialization. Multiple concurrent calls
    /// during startup will block briefly but return the same cached value.
    /// </para>
    /// <para>
    /// <b>Kubernetes Format:</b> When running in Kubernetes, the pod name
    /// typically follows the pattern: "{deployment-name}-{pod-hash}-{random-suffix}"
    /// For example: "chat-api-7d9f4c5b6d-abc12"
    /// </para>
    /// </remarks>
    /// <example>
    /// Usage in a command handler:
    /// <code><![CDATA[
    /// var originPodId = _podIdentityService.PodId;
    /// var chatEvent = new ChatEventDto
    /// {
    ///     MessageId = Guid.NewGuid(),
    ///     // ... other properties ...
    ///     OriginPodId = originPodId
    /// };
    /// ]]></code>
    /// </example>
    public string PodId
    {
        get
        {
            // Double-check locking pattern for thread-safe lazy initialization
            if (_cachedPodId is not null)
            {
                return _cachedPodId;
            }

            lock (_lock)
            {
                // Check again inside the lock in case another thread initialized it
                if (_cachedPodId is not null)
                {
                    return _cachedPodId;
                }

                // Resolve the pod ID from environment variables
                _cachedPodId = ResolvePodId();
                return _cachedPodId;
            }
        }
    }

    /// <summary>
    /// Resolves the pod identifier from environment variables.
    /// </summary>
    /// <returns>
    /// A string containing the resolved pod identifier.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method implements the priority order for environment variable
    /// resolution, checking each variable in sequence and returning the
    /// first non-empty value found.
    /// </para>
    /// <para>
    /// <b>Priority Order:</b>
    /// <list type="number">
    /// <item><c>POD_NAME</c> - Explicit Kubernetes configuration</item>
    /// <item><c>HOSTNAME</c> - Automatic Kubernetes environment variable</item>
    /// <item><c>COMPUTERNAME</c> - Windows fallback</item>
    /// <item><c>MACHINE_NAME</c> - Generic fallback</item>
    /// <item><c>"localhost"</c> - Development fallback</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Validation:</b> All environment variable values are trimmed and
    /// validated to ensure they are not empty or whitespace-only before
    /// being accepted as the pod ID.
    /// </para>
    /// </remarks>
    private static string ResolvePodId()
    {
        // Priority 1: POD_NAME - Set via Kubernetes Downward API
        var podName = GetEnvironmentVariable("POD_NAME");
        if (!string.IsNullOrWhiteSpace(podName))
        {
            return podName!;
        }

        // Priority 2: HOSTNAME - Automatically set in Kubernetes pods
        var hostname = GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            return hostname!;
        }

        // Priority 3: COMPUTERNAME - Windows fallback
        var computerName = GetEnvironmentVariable("COMPUTERNAME");
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            return computerName!;
        }

        // Priority 4: MACHINE_NAME - Generic fallback
        var machineName = GetEnvironmentVariable("MACHINE_NAME");
        if (!string.IsNullOrWhiteSpace(machineName))
        {
            return machineName!;
        }

        // Priority 5: Development fallback
        return "localhost";
    }

    /// <summary>
    /// Gets an environment variable value with trimming and null handling.
    /// </summary>
    /// <param name="variableName">
    /// The name of the environment variable to retrieve.
    /// </param>
    /// <returns>
    /// The trimmed environment variable value, or <c>null</c> if the
    /// variable is not set or contains only whitespace.
    /// </returns>
    /// <remarks>
    /// This helper method centralizes environment variable access,
    /// providing consistent trimming and null-coalescing behavior.
    /// </remarks>
    private static string? GetEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return value?.Trim();
    }
}
