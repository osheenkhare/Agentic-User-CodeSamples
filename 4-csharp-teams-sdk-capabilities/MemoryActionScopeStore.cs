using System.Collections.Concurrent;

internal sealed record MemoryActionScope(
    string Id,
    ConversationMemoryScope Scope,
    string RequesterId,
    string ConversationId,
    DateTimeOffset ExpiresAt);

internal sealed class MemoryActionScopeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, MemoryActionScope> scopes = new();

    public MemoryActionScope Create(
        ConversationMemoryScope scope,
        string requesterId,
        string conversationId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        RemoveExpired();

        MemoryActionScope actionScope = new(
            Guid.NewGuid().ToString("N"),
            scope,
            requesterId,
            conversationId,
            DateTimeOffset.UtcNow.Add(Lifetime));
        if (!scopes.TryAdd(actionScope.Id, actionScope))
        {
            throw new InvalidOperationException(
                "Could not create a unique memory action scope.");
        }

        return actionScope;
    }

    public bool TryConsume(
        string id,
        string requesterId,
        string conversationId,
        out ConversationMemoryScope? scope)
    {
        scope = null;
        if (!Guid.TryParseExact(id, "N", out _)
            || !scopes.TryGetValue(id, out MemoryActionScope? actionScope)
            || actionScope.ExpiresAt <= DateTimeOffset.UtcNow
            || !actionScope.RequesterId.Equals(requesterId, StringComparison.Ordinal)
            || !actionScope.ConversationId.Equals(
                conversationId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!scopes.TryRemove(
            new KeyValuePair<string, MemoryActionScope>(id, actionScope)))
        {
            return false;
        }

        scope = actionScope.Scope;
        return true;
    }

    private void RemoveExpired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string id, MemoryActionScope scope) in scopes)
        {
            if (scope.ExpiresAt <= now)
            {
                scopes.TryRemove(
                    new KeyValuePair<string, MemoryActionScope>(id, scope));
            }
        }
    }
}
