using System.Diagnostics;
using System.Text;
using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Schema;

/// <summary>
/// Emulates streaming by periodically replacing one Teams message with the
/// complete response accumulated so far.
/// </summary>
public static class EditBasedStreaming
{
    /// <summary>
    /// Sends a placeholder, consumes response chunks, and updates the same
    /// activity at a bounded cadence.
    /// </summary>
    /// <param name="context">The current Teams message context.</param>
    /// <param name="chunks">Response text chunks from a model or other producer.</param>
    /// <param name="updateInterval">Minimum time between intermediate message updates.</param>
    /// <param name="placeholder">Text shown before the first response update.</param>
    /// <param name="cancellationToken">Stops generation and message updates.</param>
    public static async Task SendAsync(
        Context<MessageActivity> context,
        IAsyncEnumerable<string> chunks,
        TimeSpan? updateInterval = null,
        string placeholder = "Working...",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(chunks);

        TimeSpan interval = updateInterval ?? TimeSpan.FromSeconds(1);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updateInterval),
                "The update interval must be greater than zero.");
        }

        string conversationId = context.Activity.Conversation?.Id
            ?? throw new InvalidOperationException("Incoming message has no conversation ID.");

        Microsoft.Teams.Core.SendActivityResponse? placeholderActivity =
            await context.SendAsync(placeholder, cancellationToken);
        string placeholderActivityId = placeholderActivity?.Id
            ?? throw new InvalidOperationException(
                "Teams did not return an activity ID for the placeholder message.");

        StringBuilder response = new();
        int publishedLength = 0;
        Stopwatch updateClock = Stopwatch.StartNew();

        await foreach (string chunk in chunks.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            response.Append(chunk);

            if (updateClock.Elapsed < interval)
            {
                continue;
            }

            await UpdateMessageAsync(response.ToString());
            publishedLength = response.Length;
            updateClock.Restart();
        }

        if (response.Length == 0)
        {
            throw new InvalidOperationException("The response source produced no text.");
        }

        if (publishedLength != response.Length)
        {
            await UpdateMessageAsync(response.ToString());
        }

        async Task UpdateMessageAsync(string text)
        {
            await context.Api.Conversations.Activities.UpdateAsync(
                conversationId,
                placeholderActivityId,
                new MessageActivity(text),
                cancellationToken: cancellationToken);
        }
    }
}
