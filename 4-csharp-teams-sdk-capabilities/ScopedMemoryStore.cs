using System.Collections.Concurrent;

internal sealed record MemoryFact(
    string Id,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

internal sealed class ScopedMemoryStore
{
    private const int MaximumFactLength = 400;
    private readonly ConcurrentDictionary<string, ScopeBucket> buckets = new();
    private readonly TimeSpan lifetime;
    private readonly int maximumItemsPerScope;

    public ScopedMemoryStore(IConfiguration configuration)
    {
        int lifetimeMinutes = configuration.GetValue("Memory:LifetimeMinutes", 60);
        lifetime = TimeSpan.FromMinutes(Math.Clamp(lifetimeMinutes, 5, 1440));
        maximumItemsPerScope = Math.Clamp(
            configuration.GetValue("Memory:MaximumItemsPerScope", 20),
            1,
            100);
    }

    public MemoryFact Add(ConversationMemoryScope scope, string text)
    {
        ArgumentNullException.ThrowIfNull(scope);
        string normalized = NormalizeFact(text);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MemoryFact fact = new(
            Guid.NewGuid().ToString("N"),
            normalized,
            now,
            now.Add(lifetime));
        ScopeBucket bucket = buckets.GetOrAdd(scope.Key, _ => new());

        lock (bucket.Gate)
        {
            RemoveExpired(bucket, now);
            bucket.Facts.RemoveAll(existing =>
                existing.Text.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            bucket.Facts.Add(fact);
            int excess = bucket.Facts.Count - maximumItemsPerScope;
            if (excess > 0)
            {
                bucket.Facts.RemoveRange(0, excess);
            }
        }

        return fact;
    }

    public IReadOnlyList<MemoryFact> Get(ConversationMemoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!buckets.TryGetValue(scope.Key, out ScopeBucket? bucket))
        {
            return [];
        }

        lock (bucket.Gate)
        {
            RemoveExpired(bucket, DateTimeOffset.UtcNow);
            return bucket.Facts.ToArray();
        }
    }

    public int Clear(ConversationMemoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!buckets.TryGetValue(scope.Key, out ScopeBucket? bucket))
        {
            return 0;
        }

        lock (bucket.Gate)
        {
            int removed = bucket.Facts.Count;
            bucket.Facts.Clear();
            return removed;
        }
    }

    private static void RemoveExpired(ScopeBucket bucket, DateTimeOffset now) =>
        bucket.Facts.RemoveAll(fact => fact.ExpiresAt <= now);

    private static string NormalizeFact(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalized = string.Join(
            " ",
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length > MaximumFactLength)
        {
            throw new ArgumentException(
                $"Memory facts cannot exceed {MaximumFactLength} characters.",
                nameof(text));
        }

        return normalized.TrimEnd('.', ' ', '\t', '\r', '\n');
    }

    private sealed class ScopeBucket
    {
        public object Gate { get; } = new();
        public List<MemoryFact> Facts { get; } = [];
    }
}
