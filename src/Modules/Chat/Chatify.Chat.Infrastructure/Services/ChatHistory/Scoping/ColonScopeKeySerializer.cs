using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Domain;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.Scoping;

/// <summary>
/// Default implementation of <see cref="IScopeKeySerializer"/> using colon separation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Format:</b> "{ScopeType}:{ScopeId}"
/// </para>
/// <para>
/// <b>Examples:</b>
/// <list type="bullet">
/// <item>"DirectMessage:user123"</item>
/// <item>"Group:team-456"</item>
/// <item>"Channel:general"</item>
/// </list>
/// </para>
/// <para>
/// <b>Design Notes:</b> This format is human-readable and works well with Cassandra
/// clustering when the scope type comes first. If a different format is needed
/// (e.g., pipe-separated, URL-encoded), create a new implementation of
/// <see cref="IScopeKeySerializer"/> and register it in DI.
/// </para>
/// </remarks>
public sealed class ColonScopeKeySerializer : IScopeKeySerializer
{
    private const char Separator = ':';

    /// <inheritdoc/>
    public string Serialize(ChatScopeTypeEnum scopeType, string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentException("Scope ID cannot be null or empty.", nameof(scopeId));
        }

        return $"{scopeType}{Separator}{scopeId}";
    }

    /// <inheritdoc/>
    public (ChatScopeTypeEnum ScopeType, string ScopeId) Deserialize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentNullException(nameof(value), "Scope key cannot be null or empty.");
        }

        var separatorIndex = value.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            throw new FormatException($"Invalid scope key format: '{value}'. Expected '{Separator}' to separate scope type and ID.");
        }

        var typePart = value.AsSpan(0, separatorIndex);
        var idPart = value.AsSpan(separatorIndex + 1);

        if (!Enum.TryParse<ChatScopeTypeEnum>(typePart, out var scopeType))
        {
            throw new FormatException($"Invalid scope type '{typePart.ToString()}' in scope key: '{value}'.");
        }

        var scopeId = idPart.ToString();
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new FormatException($"Scope ID cannot be empty in scope key: '{value}'.");
        }

        return (scopeType, scopeId);
    }
}
