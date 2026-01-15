using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Application.Common.Errors;
using Chatify.Chat.Application.Ports;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Chatify.Chat.Infrastructure.Services.RateLimit;

/// <summary>
/// Redis-based implementation of <see cref="IRateLimitService"/> for enforcing
/// rate limits using a fixed window counter algorithm with atomic Lua scripts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service provides distributed rate limiting using Redis
/// as the backing store, ensuring accurate rate limit enforcement across multiple
/// pod instances in a distributed deployment.
/// </para>
/// <para>
/// <b>Algorithm:</b> Fixed window counter implemented with atomic Lua scripts:
/// <list type="bullet">
/// <item>Key pattern: <c>rl:{userId}:{endpoint}:{window}</c></item>
/// <item>Data structure: Redis string counter with TTL</item>
/// <item>Atomic operation: Lua script performs INCR + EXPIRE atomically</item>
/// <item>Window management: TTL automatically expires old windows</item>
/// </list>
/// </para>
/// <para>
/// <b>Fixed Window:</b> The fixed window approach provides simple rate limiting
/// where requests are counted within fixed time intervals (e.g., per minute).
/// A window of 60 seconds with a threshold of 100 means "no more than 100 requests
/// in each 60-second window." Windows reset at fixed boundaries (e.g., 12:00:00,
/// 12:01:00, 12:02:00).
/// </para>
/// <para>
/// <b>Atomic Operations:</b> Using Lua ensures the entire INCR + EXPIRE operation
/// is atomic, preventing race conditions in distributed environments. The script
/// returns whether the request is allowed in a single round trip.
/// </para>
/// <para>
/// <b>Distribution:</b> Using Redis ensures all pods see the same counter values,
/// preventing users from circumventing limits by distributing requests across
/// multiple pods.
/// </para>
/// <para>
/// <b>Key Format:</b> Rate limit keys follow the pattern:
/// <c>rl:{userId}:{endpoint}:{window}</c>
/// where <c>{window}</c> is the window duration in seconds. This ensures
/// each endpoint/user combination has independent counters with proper TTL.
/// </para>
/// </remarks>
public sealed class RateLimitService : IRateLimitService
{
    /// <summary>
    /// The Lua script for atomic rate limit checking and incrementing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Script Logic:</b>
    /// <list type="number">
    /// <item>Get the current count using KEYS[1]</item>
    /// <item>If count is nil (key doesn't exist), initialize to 0</item>
    /// <item>If count is less than the threshold (ARGV[1]), increment and return 1 (allowed)</item>
    /// <item>If count is at or above threshold, return 0 (blocked)</item>
    /// <item>Set expiration to window duration (ARGV[2]) seconds</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    /// <item>KEYS[1]: The rate limit key (e.g., "rl:user123:SendMessage:60")</item>
    /// <item>ARGV[1]: The threshold (maximum allowed requests)</item>
    /// <item>ARGV[2]: The window duration in seconds for TTL</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Return Values:</b>
    /// <list type="bullet">
    /// <item>1: Request is allowed (count was below threshold, now incremented)</item>
    /// <item>0: Request is blocked (count is at or above threshold)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Atomicity:</b> This entire script executes atomically on the Redis server,
    /// ensuring no race conditions between the check, increment, and expire operations.
    /// </para>
    /// </remarks>
    private const string LuaScript = """
        local current = redis.call('GET', KEYS[1])
        if current == false then
            current = 0
        else
            current = tonumber(current)
        end
        if current < tonumber(ARGV[1]) then
            redis.call('INCR', KEYS[1])
            redis.call('EXPIRE', KEYS[1], ARGV[2])
            return 1
        else
            return 0
        end
        """;

    /// <summary>
    /// Gets the Redis connection multiplexer for database operations.
    /// </summary>
    /// <remarks>
    /// The multiplexer provides thread-safe access to Redis connections.
    /// </remarks>
    private readonly IConnectionMultiplexer _connectionMultiplexer;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<RateLimitService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitService"/> class.
    /// </summary>
    /// <param name="connectionMultiplexer">
    /// The Redis connection multiplexer for database operations. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="connectionMultiplexer"/> or <paramref name="logger"/> is null.
    /// </exception>
    /// <remarks>
    /// The constructor validates dependencies and logs initialization.
    /// </remarks>
    public RateLimitService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RateLimitService> logger)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation(
            "RateLimitService initialized with Redis backend");
    }

    /// <summary>
    /// Checks and increments the rate limit counter for a specific key using
    /// an atomic Lua script.
    /// </summary>
    /// <param name="key">
    /// The unique key to rate limit. Must follow the pattern
    /// <c>rl:{userId}:{endpoint}:{window}</c> for proper TTL management.
    /// Must not be null or empty.
    /// </param>
    /// <param name="threshold">
    /// The maximum number of allowed operations within the time window.
    /// Must be positive.
    /// </param>
    /// <param name="windowSeconds">
    /// The duration of the rate limit window in seconds for TTL.
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
    /// <exception cref="RedisException">
    /// Thrown when a Redis connection error occurs.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Algorithm:</b> This method implements the fixed window counter algorithm:
    /// <list type="number">
    /// <item>Execute Lua script atomically: check count, increment if below threshold, set TTL</item>
    /// <item>If script returns 1, request is allowed (count was below threshold)</item>
    /// <item>If script returns 0, request is blocked (count at or above threshold)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Atomicity:</b> The Lua script ensures that check, increment, and expire
    /// operations happen atomically. Without this, concurrent requests could race
    /// and exceed the threshold.
    /// </para>
    /// <para>
    /// <b>Key Format:</b> The key must include the window duration in the key itself:
    /// <c>rl:{userId}:{endpoint}:{windowSeconds}</c>
    /// This ensures that if the window configuration changes, old counters won't
    /// interfere with new ones.
    /// </para>
    /// <para>
    /// <b>TTL Management:</b> The EXPIRE command in the Lua script ensures that
    /// old counters are automatically cleaned up. Keys expire after the window
    /// duration, preventing memory bloat.
    /// </para>
    /// <para>
    /// <b>Usage Example:</b>
    /// <code><![CDATA[
    /// // Check rate limit for sending messages
    /// var result = await _rateLimitService.CheckAndIncrementAsync(
    ///     "rl:user123:SendMessage:60",
    ///     threshold: 100,
    ///     windowSeconds: 60,
    ///     cancellationToken);
    ///
    /// if (result.IsFailure)
    /// {
    ///     _logger.LogWarning("Rate limit exceeded for user {UserId}", "user123");
    ///     return ResultEntity.Failure(result.Error);
    /// }
    ///
    /// // Proceed with the operation
    /// ]]></code>
    /// </para>
    /// </remarks>
    public async Task<ResultEntity> CheckAndIncrementAsync(
        string key,
        int threshold,
        int windowSeconds,
        CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Rate limit key cannot be empty.", nameof(key));
        }

        if (threshold <= 0)
        {
            throw new ArgumentException(
                $"Threshold must be positive. Provided: {threshold}",
                nameof(threshold));
        }

        if (windowSeconds <= 0)
        {
            throw new ArgumentException(
                $"Window duration must be positive. Provided: {windowSeconds}",
                nameof(windowSeconds));
        }

        var database = _connectionMultiplexer.GetDatabase();

        try
        {
            _logger.LogDebug(
                "Checking rate limit: Key={Key}, Threshold={Threshold}, Window={Window}s",
                key,
                threshold,
                windowSeconds);

            var result = await database.ScriptEvaluateAsync(
                LuaScript,
                new RedisKey[] { new(key) },
                new RedisValue[] { threshold, windowSeconds });

            var isAllowed = (long)result == 1;

            if (isAllowed)
            {
                _logger.LogDebug(
                    "Rate limit check passed: Key={Key}, Threshold={Threshold}, Window={Window}s",
                    key,
                    threshold,
                    windowSeconds);

                return ResultEntity.Success();
            }
            else
            {
                _logger.LogWarning(
                    "Rate limit exceeded: Key={Key}, Threshold={Threshold}, Window={Window}s",
                    key,
                    threshold,
                    windowSeconds);

                return ResultEntity.Failure(
                    ServiceError.Chat.RateLimitExceeded(key, null));
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(
                ex,
                "Redis error during rate limit check: Key={Key}, Threshold={Threshold}, Window={Window}s",
                key,
                threshold,
                windowSeconds);

            return ResultEntity.Failure(
                ServiceError.System.ConfigurationError("Failed to check rate limit due to Redis error.", ex));
        }
    }
}
