using System.ComponentModel.DataAnnotations;

namespace Rynco.Rikki.Db;

/// <summary>
/// The state of a pull request.
/// </summary>
public enum PullRequestState
{
    /// <summary>
    /// The pull request is open.
    /// </summary>
    Open = 0,

    /// <summary>
    /// The pull request has been added into a merge queue.
    /// </summary>
    Enqueued = 1,

    /// <summary>
    /// The pull request has been merged.
    /// </summary>
    Merged = 2
}

/// <summary>
/// Represents a merge queue for a repository.
/// </summary>
public sealed record MergeQueue
{
    /// <summary>
    /// The internal ID of the merge queue.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The repository this merge queue is for. Should be the string ID in the config.
    /// </summary>
    public required string RepoId { get; set; }

    /// <summary>
    /// The target branch for this merge queue. This is the branch that will be updated when CI
    /// passed.
    /// </summary>
    public required string TargetBranch { get; set; }

    /// <summary>
    /// The working branch for this merge queue. This is the branch where PRs are first merged into
    /// before running CI.
    /// </summary>
    public required string WorkingBranch { get; set; }

    /// <summary>
    /// The head sequence number for this merge queue, representing the oldest PR still not merged
    /// in the queue. PRs with a smaller sequence number than this one are already merged.
    /// </summary>
    public required int HeadSequenceNumber { get; set; }

    /// <summary>
    /// The tail sequence number for this merge queue, representing the next sequence number to be
    /// allocated in this queue. No PRs should have a sequence number equal to or greater than this.
    /// The contents of the merge queue are the PRs with sequence numbers between the tail and head.
    /// </summary>
    public required int TailSequenceNumber { get; set; }
}

/// <summary>
/// Represents a pull request that participates in a merge queue.
/// Note that the branch for the merge queue itself is not represented here --
/// it's just for those pull requests that are to be merged into a target branch.
/// </summary>
public sealed record PullRequest
{
    /// <summary>
    /// The internal ID of the pull request.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The repository this pull request is for.
    /// </summary>
    public required string RepoId { get; set; }

    /// <summary>
    /// The source branch of the pull request.
    /// </summary>
    public required string SourceBranch { get; set; }

    /// <summary>
    /// The target branch of the pull request.
    /// </summary>
    public required string TargetBranch { get; set; }

    /// <summary>
    /// The merge queue this pull request is in.
    /// </summary>
    public required int MergeQueueId { get; set; }

    /// <summary>
    /// The pull request number.
    /// </summary>
    public required int Number { get; set; }

    /// <summary>
    /// The priority of the pull request, higher priority is merged first.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// The CI information associated with the pull request.
    /// </summary>
    public EnqueuedPullRequest? CiInfo { get; set; }
}

public sealed record EnqueuedPullRequest
{
    /// <summary>
    /// The associated pull request ID.
    /// </summary>
    public required int PullRequestId { get; set; }

    /// <summary>
    /// The sequence number of the pull request in the merge queue. See <see cref="MergeQueue"/>.
    /// </summary>
    public required int SequenceNumber { get; set; }

    /// <summary>
    /// The branch associated with the merge queue CI of the pull request.
    /// This is not the source branch of the pull request.
    /// </summary>
    public required string AssociatedBranch { get; set; }

    /// <summary>
    /// The commit SHA of the pull request in the merge queue.
    /// </summary>
    public required string MqCommit { get; set; }

    /// <summary>
    /// The CI number allocated to the pull request. This should be associated with the repository
    /// of the pull request.
    /// </summary>
    public int? CiNumber { get; set; }

    /// <summary>
    /// Whether the CI has finished.
    /// </summary>
    public bool Finished { get; set; } = false;

    /// <summary>
    /// Whether the CI has passed.
    /// </summary>
    public bool Passed { get; set; } = false;
}
