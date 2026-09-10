using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Schema;
using Microsoft.Teams.Cards;
using Microsoft.Teams.Common;

/// <summary>
/// Status values displayed by a work-plan step.
/// </summary>
public enum WorkPlanStatus
{
    /// <summary>The step has not started.</summary>
    Pending,

    /// <summary>The step is currently running.</summary>
    InProgress,

    /// <summary>The step completed.</summary>
    Done,
}

/// <summary>
/// A short user-visible unit of work and its current status.
/// </summary>
/// <param name="Title">Text displayed for the step.</param>
/// <param name="Status">Current execution status.</param>
public sealed record WorkPlanStep(string Title, WorkPlanStatus Status)
{
    /// <summary>
    /// Gets or sets the current execution status.
    /// </summary>
    public WorkPlanStatus Status { get; set; } = Status;
}

/// <summary>
/// Builds a collapsible Adaptive Card for a user-visible work plan.
/// </summary>
public static class WorkPlanCard
{
    /// <summary>Maximum number of steps displayed by one card.</summary>
    public const int MaxSteps = 8;

    /// <summary>Maximum supported plan title length.</summary>
    public const int MaxTitleLength = 60;

    private const string MarkerColumnWidth = "20px";
    private const string TasksContainerId = "tasks-container";
    private const string ChevronDownId = "chevron-ChevronDown";
    private const string ChevronRightId = "chevron-ChevronRight";

    /// <summary>
    /// Creates a Teams message containing the current work-plan card.
    /// </summary>
    /// <param name="steps">Ordered steps to display.</param>
    /// <param name="title">Optional heading; blank values use "Task plan".</param>
    public static MessageActivity CreateMessage(
        IReadOnlyList<WorkPlanStep> steps,
        string? title = null) =>
        new MessageActivity().AddAttachment(
            TeamsAttachment.CreateBuilder()
                .WithAdaptiveCard(Build(steps, title))
                .Build());

    /// <summary>
    /// Builds a collapsible work-plan Adaptive Card.
    /// </summary>
    /// <param name="steps">Ordered steps to display.</param>
    /// <param name="title">Optional heading; blank values use "Task plan".</param>
    public static AdaptiveCard Build(
        IReadOnlyList<WorkPlanStep> steps,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count is < 1 or > MaxSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(steps),
                $"A work plan must contain between 1 and {MaxSteps} steps.");
        }

        string planTitle = string.IsNullOrWhiteSpace(title) ? "Task plan" : title.Trim();
        if (planTitle.Length > MaxTitleLength)
        {
            throw new ArgumentException(
                $"The plan title cannot exceed {MaxTitleLength} characters.",
                nameof(title));
        }

        int completed = steps.Count(step => step.Status == WorkPlanStatus.Done);
        bool expanded = completed != steps.Count;

        ColumnSet header = new ColumnSet()
            .WithColumns(
                CreateChevron("ChevronDown", ChevronDownId, expanded),
                CreateChevron("ChevronRight", ChevronRightId, !expanded),
                new Column()
                    .WithWidth(Width("stretch"))
                    .WithItems(
                        new TextBlock()
                            .WithText($"{planTitle} — {completed}/{steps.Count} done")
                            .WithWeight(TextWeight.Bolder)
                            .WithWrap(true)))
            .WithSelectAction(
                new ToggleVisibilityAction().WithTargetElements(
                    new Union<IList<string>, IList<TargetElement>>(
                        new List<string>
                        {
                            TasksContainerId,
                            ChevronDownId,
                            ChevronRightId,
                        })));

        Container stepList = new Container()
            .WithId(TasksContainerId)
            .WithIsVisible(expanded)
            .WithItems(steps.Select(CreateStepRow).Cast<CardElement>().ToList());

        return new AdaptiveCard().WithBody(header, stepList);
    }

    private static ColumnSet CreateStepRow(WorkPlanStep step)
    {
        if (string.IsNullOrWhiteSpace(step.Title))
        {
            throw new ArgumentException("Work-plan step titles cannot be blank.", nameof(step));
        }

        return new ColumnSet().WithColumns(
            new Column()
                .WithWidth(Width(MarkerColumnWidth))
                .WithVerticalContentAlignment(VerticalAlignment.Center)
                .WithItems(CreateStepMarker(step.Status)),
            new Column()
                .WithWidth(Width("stretch"))
                .WithItems(
                    new TextBlock()
                        .WithText(step.Title)
                        .WithWrap(true)
                        .WithIsSubtle(step.Status == WorkPlanStatus.Pending)));
    }

    private static CardElement CreateStepMarker(WorkPlanStatus status)
    {
        if (status == WorkPlanStatus.InProgress)
        {
            return new ProgressRing().WithSize(ProgressRingSize.Tiny);
        }

        bool completed = status == WorkPlanStatus.Done;
        return new Icon()
            .WithName(completed ? "CheckmarkCircle" : "Circle")
            .WithSize(IconSize.XSmall)
            .WithStyle(completed ? IconStyle.Filled : IconStyle.Regular)
            .WithColor(completed ? TextColor.Good : TextColor.Default);
    }

    private static Column CreateChevron(string name, string id, bool isVisible) =>
        new Column()
            .WithId(id)
            .WithWidth(Width(MarkerColumnWidth))
            .WithVerticalContentAlignment(VerticalAlignment.Center)
            .WithIsVisible(isVisible)
            .WithItems(new Icon().WithName(name).WithSize(IconSize.XSmall));

    private static IUnion<string, float> Width(string value) =>
        new Union<string, float>(value);
}
