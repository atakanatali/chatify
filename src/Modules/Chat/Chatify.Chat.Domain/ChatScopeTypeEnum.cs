namespace Chatify.Chat.Domain;

/// <summary>
/// Defines the supported scope types for chat messaging within Chatify.
/// Each scope type represents a different conversation context that determines
/// how messages are ordered and which participants can access them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Channel:</b> Represents a multi-participant chat channel where messages
/// are shared among all members. Channel messages use a channel identifier as
/// their scope ID and support pub/sub semantics for real-time distribution.
/// </para>
/// <para>
/// <b>DirectMessage:</b> Represents a one-to-one or small group conversation
/// between specific users. Direct messages use a conversation identifier as
/// their scope ID and enforce stricter access controls.
/// </para>
/// <para>
/// The scope type is critical for message ordering guarantees. All messages
/// within the same scope (identified by ScopeType + ScopeId combination) are
/// processed in strict chronological order to maintain conversation integrity.
/// </para>
/// </remarks>
public enum ChatScopeTypeEnum
{
    /// <summary>
    /// A multi-participant channel chat where messages are broadcast to all members.
    /// </summary>
    /// <remarks>
    /// Channel scopes are ideal for team communication, topic-based discussions,
    /// and public group conversations. The ScopeId for channels typically
    /// represents a channel name, UUID, or other stable identifier.
    /// </remarks>
    Channel = 0,

    /// <summary>
    /// A direct message conversation between specific users.
    /// </summary>
    /// <remarks>
    /// DirectMessage scopes support private one-to-one or small group conversations.
    /// The ScopeId for direct messages typically represents a composite key
    /// derived from participant identifiers or a conversation UUID.
    /// </remarks>
    DirectMessage = 1
}
