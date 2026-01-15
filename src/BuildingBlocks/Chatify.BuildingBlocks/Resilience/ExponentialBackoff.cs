namespace Chatify.BuildingBlocks.Resilience;

/// <summary>
/// Provides exponential backoff with jitter for retry scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Exponential backoff is a retry strategy that increases
/// the delay between retries exponentially, reducing the load on a failing
/// service and giving it time to recover. Jitter (randomness) is added to
/// prevent multiple clients from retrying simultaneously (thundering herd).
/// </para>
/// <para>
/// <b>Algorithm:</b> The delay for attempt N is calculated as:
/// <c>min(initial × 2^(N-1) + random(0, jitter), max)</c>
/// </para>
/// <para>
/// <b>Example:</b> With initial=1s, max=16s, jitter=250ms:
/// <list type="bullet">
/// <item>Attempt 1: 1s + 0-250ms = 1.0-1.25s</item>
/// <item>Attempt 2: 2s + 0-250ms = 2.0-2.25s</item>
/// <item>Attempt 3: 4s + 0-250ms = 4.0-4.25s</item>
/// <item>Attempt 4: 8s + 0-250ms = 8.0-8.25s</item>
/// <item>Attempt 5+: 16s + 0-250ms = 16.0-16.25s (capped)</item>
/// </list>
/// </para>
/// <para>
/// <b>Thread Safety:</b> This class is NOT thread-safe. Create a separate
/// instance for each concurrent retry scenario or use external synchronization.
/// </para>
/// </remarks>
public sealed class ExponentialBackoff
{
    /// <summary>
    /// Gets the initial backoff delay.
    /// </summary>
    /// <remarks>
    /// This is the delay used for the first retry. Each subsequent retry
    /// doubles this value until the maximum is reached.
    /// </remarks>
    private readonly TimeSpan _initial;

    /// <summary>
    /// Gets the maximum backoff delay.
    /// </summary>
    /// <remarks>
    /// The calculated delay will never exceed this value, even after
    /// many retry attempts.
    /// </remarks>
    private readonly TimeSpan _max;

    /// <summary>
    /// Gets the maximum jitter to add to each delay.
    /// </summary>
    /// <remarks>
    /// A random value between 0 and this amount is added to the calculated
    /// delay to prevent synchronized retries from multiple clients.
    /// </remarks>
    private readonly TimeSpan _jitter;

    /// <summary>
    /// The current retry attempt number (1-indexed).
    /// </summary>
    /// <remarks>
    /// Incremented on each call to <see cref="NextDelayWithJitter"/>.
    /// Reset to 0 when <see cref="Reset"/> is called.
    /// </remarks>
    private int _attempt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExponentialBackoff"/> class.
    /// </summary>
    /// <param name="initial">
    /// The initial backoff delay for the first retry. Must be positive.
    /// </param>
    /// <param name="max">
    /// The maximum backoff delay. Must be greater than or equal to <paramref name="initial"/>.
    /// </param>
    /// <param name="jitter">
    /// The maximum jitter to add to each delay. Default is 250ms.
    /// Must be greater than or equal to zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="initial"/> or <paramref name="max"/> is negative,
    /// or when <paramref name="max"/> is less than <paramref name="initial"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Recommended Values:</b>
    /// <list type="bullet">
    /// <item>Database retries: 100ms-10s initial, 10s-30s max</item>
    /// <item>Consumer retries: 1s-2s initial, 16s-60s max</item>
    /// <item>HTTP API calls: 100ms-500ms initial, 5s-10s max</item>
    /// </list>
    /// </para>
    /// </remarks>
    public ExponentialBackoff(TimeSpan initial, TimeSpan max, TimeSpan? jitter = null)
    {
        if (initial <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initial), "Initial delay must be positive.");
        }

        if (max < initial)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Max delay must be greater than or equal to initial delay.");
        }

        _initial = initial;
        _max = max;
        _jitter = jitter ?? TimeSpan.FromMilliseconds(250);
        _attempt = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExponentialBackoff"/> class
    /// using millisecond values.
    /// </summary>
    /// <param name="initialMs">
    /// The initial backoff delay in milliseconds. Must be positive.
    /// </param>
    /// <param name="maxMs">
    /// The maximum backoff delay in milliseconds. Must be greater than or equal to <paramref name="initialMs"/>.
    /// </param>
    /// <param name="jitterMs">
    /// The maximum jitter in milliseconds. Default is 250. Must be greater than or equal to zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any parameter is negative or when <paramref name="maxMs"/> is less than <paramref name="initialMs"/>.
    /// </exception>
    /// <remarks>
    /// This constructor is a convenience overload for specifying delays in milliseconds
    /// without needing to create <see cref="TimeSpan"/> instances.
    /// </remarks>
    public ExponentialBackoff(int initialMs, int maxMs, int jitterMs = 250)
        : this(TimeSpan.FromMilliseconds(initialMs), TimeSpan.FromMilliseconds(maxMs), TimeSpan.FromMilliseconds(jitterMs))
    {
    }

    /// <summary>
    /// Resets the retry attempt counter to zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this method after a successful operation to reset the backoff
    /// sequence for the next failure. This ensures that transient failures
    /// don't result in progressively longer delays for subsequent operations.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code><![CDATA[
    /// // Operation fails, retries with 1s, 2s, 4s delays
    /// // Eventually succeeds
    /// backoff.Reset(); // Reset for next operation
    /// // Next failure will start at 1s again, not 8s
    /// ]]></code>
    /// </para>
    /// </remarks>
    public void Reset()
    {
        _attempt = 0;
    }

    /// <summary>
    /// Calculates the next delay with exponential backoff and jitter.
    /// </summary>
    /// <returns>
    /// The delay to wait before the next retry attempt, including jitter.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Calculation:</b>
    /// <code><![CDATA[
    /// baseDelay = min(initial × 2^(attempt - 1), max)
    /// finalDelay = baseDelay + random(0, jitter)
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Side Effect:</b> This method increments the attempt counter.
    /// The first call returns delay for attempt 1, the second call returns
    /// delay for attempt 2, etc.
    /// </para>
    /// </remarks>
    public TimeSpan NextDelayWithJitter()
    {
        _attempt++;

        // Calculate base exponential backoff
        var baseMs = _initial.TotalMilliseconds * Math.Pow(2, _attempt - 1);

        // Cap at maximum
        var cappedMs = Math.Min(baseMs, _max.TotalMilliseconds);

        // Add jitter to prevent thundering herd
        var jitterMs = Random.Shared.NextDouble() * _jitter.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(cappedMs + jitterMs);
    }

    /// <summary>
    /// Gets the current attempt number (1-indexed).
    /// </summary>
    /// <value>
    /// The number of times <see cref="NextDelayWithJitter"/> has been called
    /// since the last reset. Returns 0 if no attempts have been made.
    /// </value>
    public int CurrentAttempt => _attempt;
}
