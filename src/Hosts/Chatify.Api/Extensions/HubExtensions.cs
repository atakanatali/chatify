using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Chatify.Api.Extensions;

/// <summary>
/// Extension methods for SignalR Hubs to simplify common tasks like
/// user ID extraction and context access.
/// </summary>
public static class HubExtensions
{
    /// <summary>
    /// Gets the user ID from the HubContext.
    /// tries to find "sub" claim, then "user_id" claim, and falls back to "anon_{ConnectionId}".
    /// </summary>
    /// <param name="context">The HubCallerContext.</param>
    /// <returns>The extracted user ID or an anonymous identifier.</returns>
    public static string GetUserId(this HubCallerContext context)
    {
        return context.User?.FindFirst("sub")?.Value
            ?? context.User?.FindFirst("user_id")?.Value
            ?? $"anon_{context.ConnectionId}";
    }

    /// <summary>
    /// Gets the user name from the HubContext or returns "anonymous".
    /// </summary>
    /// <param name="context">The HubCallerContext.</param>
    /// <returns>The user name or "anonymous".</returns>
    public static string GetUserName(this HubCallerContext context)
    {
        return context.User?.Identity?.Name ?? "anonymous";
    }
}
