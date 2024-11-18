using Rynco.Rikki.Db;

namespace Rynco.Rikki.GitOperator;

/// <summary>
/// High-level interface to local Git operations, including how URIs are mapped to local paths.
/// Also provides a way to mock it.
/// </summary>
public interface IGitOperator<TRepo, TBranch, TCommitId>
where TCommitId : IEquatable<TCommitId>
{
    public TCommitId ParseCommitId(string commitId);

    public string FormatCommitId(TCommitId commitId);

    /// <summary>
    /// Open a repository at the given URI. If the repository isn't already cloned, it will be cloned.
    /// Otherwise, it will be synchronized with the remote.
    /// </summary>
    /// <param name="uri"></param>
    /// <returns></returns>
    public ValueTask<TRepo> OpenAndUpdateAsync(string uri);

    /// <summary>
    /// Try to get the branch with the given name in the given repository. If the branch doesn't exist,
    /// return null.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branchName"></param>
    /// <returns></returns>
    public ValueTask<TBranch?> GetBranchAsync(TRepo repo, string branchName);

    /// <summary>
    /// Get the commit at the tip of the given branch.
    /// </summary>
    /// <param name="branch"></param>
    /// <returns></returns>
    public ValueTask<TCommitId> GetBranchTipAsync(TRepo repo, TBranch branch);

    /// <summary>
    /// Create a new branch at the given commit.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branchName"></param>
    /// <param name="commitId"></param>
    public ValueTask<TBranch> CreateBranchAtCommitAsync(TRepo repo, string branchName, TCommitId commitId);

    /// <summary>
    /// Get the commit message and committer info of the given commit.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="commitId"></param>
    /// <returns></returns>
    public ValueTask<(string, CommitterInfo)> GetCommitInfoAsync(TRepo repo, TCommitId commitId);

    /// <summary>
    /// Remove the given branch from the repository.
    /// </summary>
    /// <param name="branch"></param>
    public ValueTask RemoveBranchAsync(TRepo repo, TBranch branch);

    /// <summary>
    /// Reset the branch tip to the given commit.
    /// </summary>
    /// <param name="branch"></param>
    /// <param name="commitId"></param>
    public ValueTask ResetBranchToCommitAsync(TRepo repo, TBranch branch, TCommitId commitId);

    /// <summary>
    /// Check if the source branch can be merged into the target branch without any conflicts.
    /// </summary>
    public ValueTask<bool> CheckForMergeConflictAsync(TRepo repo, TBranch targetBranch, TBranch sourceBranch);

    /// <summary>
    /// Merge the source branch into the target branch, creating a merge commit, and then
    /// return the ID of the merge commit. If there's any conflict, return null.
    /// </summary>
    /// <param name="targetBranch"></param>
    /// <param name="commitId"></param>
    /// <returns></returns>
    public ValueTask<TCommitId?> MergeBranchesAsync(TRepo repo, TBranch targetBranch, TBranch sourceBranch, string commitMessage, CommitterInfo committerInfo);

    /// <summary>
    /// Rebase the source branch onto the target branch, and then return the ID of the new commit.
    /// If there's any conflict, return null.
    /// </summary>
    /// <param name="targetBranch"></param>
    /// <param name="sourceBranch"></param>
    /// <returns></returns>
    public ValueTask<TCommitId?> RebaseBranchesAsync(TRepo repo, TBranch targetBranch, TBranch sourceBranch);

    /// <summary>
    /// Push everything in the repository to the remote.
    /// </summary>
    /// <param name="repo"></param>
    public ValueTask PushRepoStateAsync(TRepo repo);

    /// <summary>
    /// Push the given branch to the remote.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branch"></param>
    public ValueTask PushBranchAsync(TRepo repo, TBranch branch);

    public async ValueTask<TCommitId?> PerformMergeAsync(
        TRepo repo,
        MergeStyle style,
        TBranch targetBranch,
        TBranch sourceBranch,
        string commitMessage,
        CommitterInfo committerInfo)
    {
        switch (style)
        {
            case MergeStyle.Merge:
                return await MergeBranchesAsync(repo, targetBranch, sourceBranch, commitMessage, committerInfo);
            case MergeStyle.Linear:
                return await RebaseBranchesAsync(repo, targetBranch, sourceBranch);
            case MergeStyle.SemiLinear:
                {
                    // First rebase the source branch onto the target branch,
                    // then merge the source branch into the target branch.
                    var newCommitId = await RebaseBranchesAsync(repo, targetBranch, sourceBranch);
                    if (newCommitId == null)
                    {
                        return default;
                    }
                    return await MergeBranchesAsync(repo, targetBranch, sourceBranch, commitMessage, committerInfo);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(style));
        }
    }
}
