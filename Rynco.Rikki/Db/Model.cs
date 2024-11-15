using System.ComponentModel.DataAnnotations;

namespace Rynco.Rikki.Db;

/// <summary>
/// The kind of repository. Only Gitlab is supported at the moment.
/// </summary>
public enum RepoKind
{
    Gitlab = 0
}

/// <summary>
/// The merge fashion for a repository.
/// </summary>
public enum MergeStyle
{
    /// <summary>
    /// Merge using merge commits.
    /// </summary>
    Merge = 0,

    /// <summary>
    /// Merge using rebase.
    /// </summary>
    Linear = 1,

    /// <summary>
    /// First rebase, and then create a merge commit.
    /// </summary>
    SemiLinear = 2
}

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
/// Represents a repository that Rikki is managing.
/// </summary>
public sealed class Repo
{
    /// <summary>
    /// The internal ID of the repository.
    /// </summary>
    public required int Id { get; set; }

    /// <summary>
    /// The display name of the repository.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The kind of repository.
    /// </summary>
    public required RepoKind Kind { get; set; }

    /// <summary>
    /// The URL of the repository.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Access token for the repository.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The merge style for the repository.
    /// </summary>
    public required MergeStyle MergeStyle { get; set; }
}

/// <summary>
/// Represents a merge queue for a repository.
/// </summary>
public sealed class MergeQueue
{
    /// <summary>
    /// The internal ID of the merge queue.
    /// </summary>
    public required int Id { get; set; }

    /// <summary>
    /// The repository this merge queue is for.
    /// </summary>
    public required int RepoId { get; set; }

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
    /// The head sequence number for this merge queue, representing the oldest PR in the queue.
    /// PRs with a smaller sequence number than this one are already merged.
    /// </summary>
    public required int HeadSequenceNumber { get; set; }

    /// <summary>
    /// The tail sequence number for this merge queue, representing the newest PR in the queue.
    /// No PR should have a sequence number larger than this one.
    /// The contents of the merge queue are the PRs with sequence numbers between the tail and head.
    /// </summary>
    public required int TailSequenceNumber { get; set; }
}

/// <summary>
/// Represents a pull request that participates in a merge queue.
/// Note that the branch for the merge queue itself is not represented here --
/// it's just for those pull requests that are to be merged into a target branch.
/// </summary>
public sealed class PullRequest
{
    /// <summary>
    /// The internal ID of the pull request.
    /// </summary>
    [Key]
    public required int Id { get; set; }

    /// <summary>
    /// The repository this pull request is for.
    /// </summary>
    public required int RepoId { get; set; }

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
    public required int Priority { get; set; } = 0;

    /// <summary>
    /// The sequence number within the merge queue. Non null if the PR is enqueued.
    /// </summary>
    public int? MqSequenceNumber { get; set; }

    /// <summary>
    /// The commit SHA of the pull request in the merge queue. Non null if the PR is enqueued.
    /// </summary>
    public string? MqCommitSha { get; set; }

    /// <summary>
    /// The CI ID of the commit in the merge queue. Non null if the PR is enqueued.
    /// </summary>
    public int? MqCiId { get; set; }

    /// <summary>
    /// Whether the CI has passed for the commit in the merge queue. 
    /// </summary>
    public bool MqCiPassed { get; set; }
}
