using Cassandra;
using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Domain;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Cql;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Mapping;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Scoping;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory;

/// <summary>
/// Refactored distributed database-based implementation of <see cref="IChatHistoryRepository"/>
/// with proper async/await, dependency injection, and separation of concerns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architecture Improvements:</b>
/// <list type="bullet">
/// <item><b>Async/Await:</b> Uses <c>ExecuteAsync</c> instead of synchronous <c>Execute</c></item>
/// <item><b>SRP:</b> Separated concerns into dedicated components (mapper, serializer, statement provider)</item>
/// <item><b>Push-down Filtering:</b> Time range filters are pushed to Cassandra via CQL WHERE clauses</item>
/// <item><b>Reusability:</b> Statement provider and serializer can be reused across repositories</item>
/// <item><b>Testability:</b> All dependencies are injected via interfaces</item>
/// </list>
/// </para>
/// <para>
/// <b>Table Schema:</b>
/// <code><![CDATA[
/// CREATE TABLE chat_messages (
///     scope_id text,
///     created_at_utc timestamp,
///     message_id uuid,
///     sender_id text,
///     text text,
///     origin_pod_id text,
///     broker_partition int,
///     broker_offset bigint,
///     PRIMARY KEY ((scope_id), created_at_utc, message_id)
/// ) WITH CLUSTERING ORDER BY (created_at_utc ASC);
/// ]]></code>
/// </para>
/// </remarks>
public sealed class ChatHistoryRepository : IChatHistoryRepository
{
    private readonly ISession _session;
    private readonly ICqlStatementProvider _statementProvider;
    private readonly IScopeKeySerializer _scopeKeySerializer;
    private readonly IChatEventRowMapper _rowMapper;
    private readonly ILogger<ChatHistoryRepository> _logger;

    // CQL statements
    private const string InsertCql = @"
        INSERT INTO chat_messages (
            scope_id, created_at_utc, message_id, sender_id, text,
            origin_pod_id, broker_partition, broker_offset
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?) IF NOT EXISTS;";

    private const string QueryBaseCql = @"
        SELECT scope_id, created_at_utc, message_id, sender_id, text,
               origin_pod_id, broker_partition, broker_offset
        FROM chat_messages
        WHERE scope_id = ?";

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHistoryRepository"/> class.
    /// </summary>
    /// <param name="session">
    /// The Cassandra session for executing statements. Must not be null.
    /// </param>
    /// <param name="statementProvider">
    /// The provider for prepared CQL statements. Must not be null.
    /// </param>
    /// <param name="scopeKeySerializer">
    /// The serializer for composite scope keys. Must not be null.
    /// </param>
    /// <param name="rowMapper">
    /// The mapper for converting rows to DTOs. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any parameter is null.
    /// </exception>
    public ChatHistoryRepository(
        ISession session,
        ICqlStatementProvider statementProvider,
        IScopeKeySerializer scopeKeySerializer,
        IChatEventRowMapper rowMapper,
        ILogger<ChatHistoryRepository> logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _statementProvider = statementProvider ?? throw new ArgumentNullException(nameof(statementProvider));
        _scopeKeySerializer = scopeKeySerializer ?? throw new ArgumentNullException(nameof(scopeKeySerializer));
        _rowMapper = rowMapper ?? throw new ArgumentNullException(nameof(rowMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("ChatHistoryRepository initialized");
    }

    /// <inheritdoc/>
    public async Task AppendAsync(
        ChatEventDto chatEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatEvent);

        // Serialize composite scope key
        var scopeKey = _scopeKeySerializer.Serialize(chatEvent.ScopeType, chatEvent.ScopeId);

        // Get or prepare the insert statement
        var preparedStatement = _statementProvider.GetOrPrepare(InsertCql);

        // Build bound statement with parameters
        var boundStatement = preparedStatement.Bind(
            scopeKey,
            chatEvent.CreatedAtUtc,
            chatEvent.MessageId,
            chatEvent.SenderId,
            chatEvent.Text,
            chatEvent.OriginPodId,
            (int?)null,  // broker_partition - null when writing from API
            (long?)null   // broker_offset - null when writing from API
        ).SetConsistencyLevel(ConsistencyLevel.LocalQuorum);

        // Execute asynchronously (non-blocking I/O)
        var rowSet = await _session.ExecuteAsync(boundStatement)
            .ConfigureAwait(false);

        // Check LWT result (lightweight transaction)
        var applied = rowSet.FirstOrDefault()?.GetValue<bool>("[applied]") ?? true;

        if (applied)
        {
            _logger.LogDebug(
                "Appended message {MessageId} for scope {ScopeKey}",
                chatEvent.MessageId,
                scopeKey);
        }
        else
        {
            _logger.LogDebug(
                "Message {MessageId} already exists (idempotent append)",
                chatEvent.MessageId);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatEventDto>> QueryByScopeAsync(
        ChatScopeTypeEnum scopeType,
        string scopeId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        // Serialize composite scope key
        var scopeKey = _scopeKeySerializer.Serialize(scopeType, scopeId);

        // Build CQL with push-down time range filtering
        var (cql, args) = BuildQueryWithRange(scopeKey, fromUtc, toUtc, limit);

        // Get or prepare the query statement
        var preparedStatement = _statementProvider.GetOrPrepare(cql);

        // Build bound statement
        var boundStatement = preparedStatement.Bind(args)
            .SetConsistencyLevel(ConsistencyLevel.LocalOne);

        // Execute asynchronously
        var rowSet = await _session.ExecuteAsync(boundStatement)
            .ConfigureAwait(false);

        // Map rows to DTOs
        var results = new List<ChatEventDto>(capacity: Math.Min(limit, 128));
        foreach (var row in rowSet)
        {
            results.Add(_rowMapper.Map(row));
            if (results.Count >= limit) break;
        }

        _logger.LogDebug(
            "Retrieved {Count} messages for scope {ScopeKey}",
            results.Count,
            scopeKey);

        return results;
    }

    /// <summary>
    /// Builds a parameterized CQL query with time range filtering pushed to the database.
    /// </summary>
    /// <param name="scopeKey">
    /// The composite scope key to query.
    /// </param>
    /// <param name="fromUtc">
    /// Optional start timestamp for filtering.
    /// </param>
    /// <param name="toUtc">
    /// Optional end timestamp for filtering.
    /// </param>
    /// <param name="limit">
    /// Maximum number of results to return.
    /// </param>
    /// <returns>
    /// A tuple containing the CQL string and parameter values.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Push-down Optimization:</b> Time range filters are applied in the WHERE clause
    /// rather than in application code. This reduces network traffic and CPU usage.
    /// </para>
    /// <para>
    /// <b>Clustering Key Benefit:</b> Since created_at_utc is a clustering key,
    /// Cassandra can efficiently seek to the start of the range and scan forward.
    /// </para>
    /// </remarks>
    private static (string Cql, object[] Args) BuildQueryWithRange(
        string scopeKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit)
    {
        if (fromUtc is null && toUtc is null)
        {
            // No time filter - just limit
            var cql = QueryBaseCql + " ORDER BY created_at_utc ASC LIMIT ?;";
            return (cql, new object[] { scopeKey, limit });
        }

        if (fromUtc is not null && toUtc is null)
        {
            // From date only
            var cql = QueryBaseCql + " AND created_at_utc >= ? ORDER BY created_at_utc ASC LIMIT ?;";
            return (cql, new object[] { scopeKey, fromUtc.Value, limit });
        }

        if (fromUtc is null && toUtc is not null)
        {
            // To date only
            var cql = QueryBaseCql + " AND created_at_utc <= ? ORDER BY created_at_utc ASC LIMIT ?;";
            return (cql, new object[] { scopeKey, toUtc.Value, limit });
        }

        // Both from and to dates
        var cqlBoth = QueryBaseCql + " AND created_at_utc >= ? AND created_at_utc <= ? ORDER BY created_at_utc ASC LIMIT ?;";
        return (cqlBoth, new object[] { scopeKey, fromUtc!.Value, toUtc!.Value, limit });
    }
}
