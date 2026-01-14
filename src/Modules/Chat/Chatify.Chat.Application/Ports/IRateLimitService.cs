using Chatify.BuildingBlocks.Primitives;

namespace Chatify.Chat.Application.Ports;

/// <summary>
/// Defines a contract for rate limiting operations to prevent abuse and ensure
/// fair resource allocation within the Chatify system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Port Role:</b> This is a secondary adapter port in Clean Architecture terms.
/// The application layer depends on this abstraction, while the infrastructure
/// layer provides concrete implementations (e.g., Redis with sliding windows,
/// in-memory token buckets, distributed rate limiters).
/// </para>
/// <para>
/// <b>Purpose:</b> Rate limiting protects against:
/// <list type="bullet">
/// <item>Spam and message flooding</item>
/// <item>DoS attacks that overwhelm system resources</item>
/// <item>Unfair resource consumption by aggressive users</item>
/// <item>Cost spikes from downstream service usage</item>
/// </list>
/// </para>
/// <para>
/// <b>Strategy:</b> The service typically implements a sliding window or
/// token bucket algorithm to limit operations per time window. Limits may
/// be applied per-user, per-IP, or globally depending on the implementation.
/// </para>
/// <para>
/// <b>Distribution:</b> In multi-pod deployments, rate limit state must be
/// shared across all instances to ensure accurate limiting. Distributed
/// implementations use Redis or similar stores for coordination.
/// </para>
/// </remarks>
public interface IRateLimitService
{
    /// <summary>
    /// Checks and increments the rate limit counter for a specific key.
    /// </summary>
    /// <param name="key">
    /// The unique key to rate limit. This is typically a composite of
    /// user ID, action type, and optionally scope identifier.
    /// Examples: "user-123:send-message", "user-123:channel:general".
    /// Must not be null or empty.
    /// </param>
    /// <param name="threshold">
    /// The maximum number of allowed operations within the time window.
    /// Once this threshold is reached, subsequent operations are rejected
    /// until the window expires.
    /// Must be positive.
    /// </param>
    /// <param name="windowSeconds">
    /// The duration of the rate limit window in seconds.
    /// After this period elapses, the counter resets and operations are
    /// allowed again.
    /// Must be positive.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ResultEntity"/> indicating whether the operation is allowed:
    /// <list type="bullet">
    /// <item><c>Success</c>: The operation is within the rate limit and allowed.</item>
    /// <item><c>Failure</c>: The rate limit has been exceeded. The result contains
    /// an <see cref="ErrorEntity"/> with details about the limit violation.</item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is empty, or when <paramref name="threshold"/>
    /// or <paramref name="windowSeconds"/> is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the rate limit service is not available.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Atomic Operation:</b> This method performs both the check and increment
    /// atomically to prevent race conditions in distributed environments.
    /// The operation is: "if current count < threshold, increment and allow;
    /// otherwise, deny."
    /// </para>
    /// <para>
    /// <b>Sliding Window:</b> For sliding window implementations, the window
    /// moves with time. A window of 60 seconds means "no more than N operations
    /// in any 60-second period," not "N operations per calendar minute."
    /// </para>
    /// <para>
    /// <b>Key Design:</b> Rate limit keys should be scoped appropriately:
    /// <list type="bullet">
    /// <item>"user-{id}:send-message" - Per-user message sending limit</item>
    /// <item>"user-{id}:channel:{scopeId}" - Per-user, per-channel limit</item>
    /// <item>"ip-{address}" - Per-IP limit for anonymous users</item>
    /// <item>"global:send-message" - System-wide limit</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// var result = await _rateLimit.CheckAndIncrementAsync(
    ///     $"user-{userId}:send-message",
    ///     threshold: 100,  // 100 messages
    ///     windowSeconds: 60,  // per 60 seconds
    ///     ct);
    ///
    /// if (result.IsFailure)
    /// {
    ///     _logger.LogWarning("Rate limit exceeded for user {UserId}", userId);
    ///     return ResultEntity.Failure(result.Error);
    /// }
    ///
    /// // Proceed with the operation
    /// ]]></code>
    /// </para>
    /// </remarks>
    Task<ResultEntity> CheckAndIncrementAsync(
        string key,
        int threshold,
        int windowSeconds,
        CancellationToken cancellationToken);
}
