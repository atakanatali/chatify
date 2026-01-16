using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Domain;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.Scoping;

/// <summary>
/// Defines a contract for serializing and deserializing composite scope keys.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Encapsulates the format of composite keys used in Cassandra
/// to represent chat scopes (e.g., "DirectMessage:user123" or "Group:team-456").
/// This allows the key format to change without affecting repository or domain logic.
/// </para>
/// <para>
/// <b>Strategy Pattern:</b> Different implementations can use different separators
/// or encoding schemes (e.g., colon, pipe, URL encoding) without changing consuming code.
/// </para>
/// </remarks>
public interface IScopeKeySerializer
{
    /// <summary>
    /// Serializes a scope type and scope ID into a composite key string.
    /// </summary>
    /// <param name="scopeType">
    /// The type of chat scope (e.g., DirectMessage, Group, Channel).
    /// </param>
    /// <param name="scopeId">
    /// The unique identifier for the scope (e.g., user ID, group ID).
    /// </param>
    /// <returns>
    /// A composite key string suitable for use as a Cassandra partition key.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="scopeId"/> is null or empty.
    /// </exception>
    string Serialize(ChatScopeTypeEnum scopeType, string scopeId);

    /// <summary>
    /// Deserializes a composite key string into its scope type and scope ID components.
    /// </summary>
    /// <param name="value">
    /// The composite key string to deserialize.
    /// </param>
    /// <returns>
    /// A tuple containing the parsed <see cref="ChatScopeTypeEnum"/> and scope ID.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="value"/> is not in the expected format.
    /// </exception>
    (ChatScopeTypeEnum ScopeType, string ScopeId) Deserialize(string value);
}
