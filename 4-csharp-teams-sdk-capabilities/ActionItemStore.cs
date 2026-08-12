using System.Collections.Concurrent;

internal sealed class ActionItemStore
{
    private const int MaxItemsPerConversation = 20;

    // This sample is intentionally process-local; production action items need durable storage.
    private readonly ConcurrentDictionary<string, List<string>> itemsByConversation = new();

    public void Add(string conversationId, string item)
    {
        List<string> items = itemsByConversation.GetOrAdd(conversationId, _ => []);
        lock (items)
        {
            items.Add(item);
            if (items.Count > MaxItemsPerConversation)
            {
                items.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<string> Get(string conversationId)
    {
        if (!itemsByConversation.TryGetValue(conversationId, out List<string>? items))
        {
            return [];
        }

        lock (items)
        {
            return [.. items];
        }
    }

    public void Clear(string conversationId) =>
        itemsByConversation.TryRemove(conversationId, out _);
}
