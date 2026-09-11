using System.Runtime.CompilerServices;

internal static class DemoContent
{
    private static readonly string[] Chunks =
    [
        "This ",
        "response ",
        "is produced ",
        "in small chunks, ",
        "while the agent ",
        "periodically edits ",
        "one Teams message.",
        "\n\nThis is edit-based streaming, not native Teams streaming.",
    ];

    public static async IAsyncEnumerable<string> GenerateChunksAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (string chunk in Chunks)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            yield return chunk;
        }
    }
}
