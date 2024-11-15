namespace Rynco.Rikki.GitOperator;

/// <summary>
/// High-level interface to local Git operations, including how URIs are mapped to local paths.
/// Also provides a way to mock it.
/// </summary>
public interface IGitOperator<TRepo, TBranch, TCommitId>
where TCommitId : IEquatable<TCommitId>
{
    /// <summary>
    /// Open a repository at the given URI. If the repository isn't already cloned, it will be cloned.
    /// Otherwise, it will be synchronized with the remote.
    /// </summary>
    /// <param name="uri"></param>
    /// <returns></returns>
    public TRepo OpenAndUpdate(Uri uri);

    /// <summary>
    /// Try to get the branch with the given name in the given repository. If the branch doesn't exist,
    /// return null.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branchName"></param>
    /// <returns></returns>
    public TBranch? GetBranch(TRepo repo, string branchName);

    /// <summary>
    /// Get the commit at the tip of the given branch.
    /// </summary>
    /// <param name="branch"></param>
    /// <returns></returns>
    public TCommitId GetBranchTip(TRepo repo, TBranch branch);

    /// <summary>
    /// Create a new branch at the given commit.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branchName"></param>
    /// <param name="commitId"></param>
    public void CreateBranchAtCommit(TRepo repo, string branchName, TCommitId commitId);

    /// <summary>
    /// Remove the given branch from the repository.
    /// </summary>
    /// <param name="branch"></param>
    public void RemoveBranch(TRepo repo, TBranch branch);

    /// <summary>
    /// Reset the branch tip to the given commit.
    /// </summary>
    /// <param name="branch"></param>
    /// <param name="commitId"></param>
    public void ResetBranchToCommit(TBranch branch, TCommitId commitId);

    /// <summary>
    /// Merge the source branch into the target branch, creating a merge commit, and then
    /// return the ID of the merge commit. If there's any conflict, return null.
    /// </summary>
    /// <param name="targetBranch"></param>
    /// <param name="commitId"></param>
    /// <returns></returns>
    public TCommitId? MergeBranches(TBranch targetBranch, TBranch sourceBranch, string commitMessage, string authorName, string authorEmail);

    /// <summary>
    /// Rebase the source branch onto the target branch, and then return the ID of the new commit.
    /// If there's any conflict, return null.
    /// </summary>
    /// <param name="targetBranch"></param>
    /// <param name="sourceBranch"></param>
    /// <returns></returns>
    public TCommitId? RebaseBranches(TBranch targetBranch, TBranch sourceBranch);

    /// <summary>
    /// Push everything in the repository to the remote.
    /// </summary>
    /// <param name="repo"></param>
    public void PushRepoState(TRepo repo);

    /// <summary>
    /// Push the given branch to the remote.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branch"></param>
    public void PushBranch(TRepo repo, TBranch branch);
}

