using System.Collections.Concurrent;
using System.Diagnostics;
using Cassandra;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.Cql;

/// <summary>
/// Thread-safe implementation of <see cref="ICqlStatementProvider"/> that caches
/// prepared Cassandra statements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Caching Strategy:</b> Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for
/// thread-safe statement caching. Once a statement is prepared, it is reused for
/// the lifetime of the application.
/// </para>
/// <para>
/// <b>Performance:</b> Statement preparation is expensive (network round-trip + server
/// processing). Caching reduces this to a one-time cost per unique query.
/// </para>
/// <para>
/// <b>Eviction:</b> This implementation does not evict statements from cache.
/// For applications with many dynamic queries, consider implementing an LRU cache
/// or time-based expiration.
/// </para>
/// </remarks>
public sealed partial class CqlStatementProvider : ICqlStatementProvider
{
    private readonly ISession _session;
    private readonly ConcurrentDictionary<string, PreparedStatement> _cache;
    private readonly ILogger<CqlStatementProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CqlStatementProvider"/> class.
    /// </summary>
    /// <param name="session">
    /// The Cassandra session for preparing statements. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any parameter is null.
    /// </exception>
    public CqlStatementProvider(
        ISession session,
        ILogger<CqlStatementProvider> logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new ConcurrentDictionary<string, PreparedStatement>();
    }

    /// <inheritdoc/>
    public PreparedStatement GetOrPrepare(string cql)
    {
        if (string.IsNullOrWhiteSpace(cql))
        {
            throw new ArgumentException("CQL cannot be null or empty.", nameof(cql));
        }

        // Try to get from cache first
        if (_cache.TryGetValue(cql, out var cachedStatement))
        {
            LogCacheHit(cql);
            return cachedStatement;
        }

        // Not cached, prepare the statement
        LogCacheMiss(cql);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var prepared = _session.Prepare(cql);
            stopwatch.Stop();

            // Add to cache (thread-safe)
            _cache.TryAdd(cql, prepared);

            LogStatementPrepared(cql, stopwatch.Elapsed.TotalMilliseconds);
            return prepared;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogPrepareFailed(cql, ex, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    #region Logging Helpers

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "CQL statement cache hit: {Cql}")]
    private partial void LogCacheHit(string cql);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "CQL statement cache miss, preparing: {Cql}")]
    private partial void LogCacheMiss(string cql);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "CQL statement prepared successfully in {ElapsedMs}ms: {Cql}")]
    private partial void LogStatementPrepared(string cql, double elapsedMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to prepare CQL statement in {ElapsedMs}ms: {Cql}")]
    private partial void LogPrepareFailed(string cql, Exception ex, double elapsedMs);

    #endregion
}
