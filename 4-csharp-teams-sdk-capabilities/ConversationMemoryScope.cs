using System.Security.Cryptography;
using System.Text;

internal enum MemoryScopeKind
{
    Personal,
    GroupChat,
    ChannelThread,
}

internal sealed record ConversationScopeInput(
    string TenantId,
    string ConversationType,
    string ConversationId,
    string UserId,
    string AgentId,
    string? TeamId = null,
    string? ChannelId = null,
    string? RootMessageId = null);

internal sealed record ConversationMemoryScope(
    string Key,
    string DisplayId,
    string Label,
    MemoryScopeKind Kind);

internal sealed class ConversationScopeResolver
{
    public ConversationMemoryScope Resolve(ConversationScopeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string tenantId = Require(input.TenantId, nameof(input.TenantId));
        string conversationType = Require(
            input.ConversationType,
            nameof(input.ConversationType));
        string key;
        string label;
        MemoryScopeKind kind;

        if (conversationType.Equals("personal", StringComparison.OrdinalIgnoreCase))
        {
            key = Join(
                "personal",
                tenantId,
                Require(input.UserId, nameof(input.UserId)),
                Require(input.AgentId, nameof(input.AgentId)));
            label = "Private 1:1";
            kind = MemoryScopeKind.Personal;
        }
        else if (conversationType.Equals("groupChat", StringComparison.OrdinalIgnoreCase))
        {
            key = Join(
                "group",
                tenantId,
                Require(input.ConversationId, nameof(input.ConversationId)));
            label = "Group chat";
            kind = MemoryScopeKind.GroupChat;
        }
        else if (conversationType.Equals("channel", StringComparison.OrdinalIgnoreCase))
        {
            key = Join(
                "channel-thread",
                tenantId,
                Require(input.TeamId, nameof(input.TeamId)),
                Require(input.ChannelId, nameof(input.ChannelId)),
                Require(input.RootMessageId, nameof(input.RootMessageId)));
            label = "Channel thread";
            kind = MemoryScopeKind.ChannelThread;
        }
        else
        {
            throw new ArgumentException(
                $"Unsupported Teams conversation type `{conversationType}`.",
                nameof(input));
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string displayId = Convert.ToHexString(digest)[..12].ToLowerInvariant();
        return new ConversationMemoryScope(key, displayId, label, kind);
    }

    private static string Join(params string[] parts) =>
        string.Join("|", parts.Select(part => $"{part.Length}:{part}"));

    private static string Require(string? value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException(
                "A required scope identifier was unavailable.",
                parameterName);
}
