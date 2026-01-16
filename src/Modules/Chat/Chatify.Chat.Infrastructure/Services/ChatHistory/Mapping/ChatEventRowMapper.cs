using System.Diagnostics;
using Cassandra;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Scoping;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.Mapping;

/// <summary>
/// Default implementation of <see cref="IChatEventRowMapper"/> for mapping
/// Cassandra rows to chat event DTOs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mapping Strategy:</b> This mapper reads all columns from the Cassandra row
/// and constructs a <see cref="ChatEventDto"/>. The composite scope_id column
/// is deserialized using the <see cref="IScopeKeySerializer"/>.
/// </para>
/// <para>
/// <b>Column Mapping:</b>
/// <list type="table">
/// <listheader>
/// <term>Cassandra Column</term>
/// <description>DTO Property</description>
/// </listheader>
/// <item>
/// <term>scope_id</term>
/// <description>Deserialized to ScopeType + ScopeId</description>
/// </item>
/// <item>
/// <term>created_at_utc</term>
/// <description>CreatedAtUtc (DateTimeOffset)</description>
/// </item>
/// <item>
/// <term>message_id</term>
/// <description>MessageId (Guid)</description>
/// </item>
/// <item>
/// <term>sender_id</term>
/// <description>SenderId (string)</description>
/// </item>
/// <item>
/// <term>text</term>
/// <description>Text (string)</description>
/// </item>
/// <item>
/// <term>origin_pod_id</term>
/// <description>OriginPodId (string)</description>
/// </item>
/// <item>
/// <term>broker_partition</term>
/// <description>BrokerPartition (int?)</description>
/// </item>
/// <item>
/// <term>broker_offset</term>
/// <description>BrokerOffset (long?)</description>
/// </item>
/// </list>
/// </para>
/// </remarks>
public sealed class ChatEventRowMapper : IChatEventRowMapper
{
    private readonly IScopeKeySerializer _scopeKeySerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatEventRowMapper"/> class.
    /// </summary>
    /// <param name="scopeKeySerializer">
    /// The serializer for composite scope keys. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scopeKeySerializer"/> is null.
    /// </exception>
    public ChatEventRowMapper(IScopeKeySerializer scopeKeySerializer)
    {
        _scopeKeySerializer = scopeKeySerializer
            ?? throw new ArgumentNullException(nameof(scopeKeySerializer));
    }

    /// <inheritdoc/>
    public ChatEventDto Map(Row row)
    {
        if (row == null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        // Extract and deserialize the composite scope key
        var scopeIdValue = row.GetValue<string>("scope_id");
        var (scopeType, scopeId) = _scopeKeySerializer.Deserialize(scopeIdValue);

        return new ChatEventDto
        {
            // Deserialized composite key
            ScopeType = scopeType,
            ScopeId = scopeId,

            // Direct column mappings
            MessageId = row.GetValue<Guid>("message_id"),
            SenderId = row.GetValue<string>("sender_id"),
            Text = row.GetValue<string>("text"),
            CreatedAtUtc = row.GetValue<DateTime>("created_at_utc"),
            OriginPodId = row.GetValue<string>("origin_pod_id")
        };
    }
}
