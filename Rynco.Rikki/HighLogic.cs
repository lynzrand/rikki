using Rynco.Rikki.Db;
using Rynco.Rikki.GitOperator;
using Rynco.Rikki.VcsHostService;

namespace Rynco.Rikki;

/// <summary>
/// The core state machine of the application.
/// 
/// This class should not hold any state itself, but operate in responce to callbacks, and call
/// the appropriate methods on the actual states held by DB, Git and VCS hosting services. In this 
/// sense this class is disposable, as it's not required to be kept around.
/// </summary>
public sealed class HighLogic<TRepo, TBranch, TCommitId>(
    HighDb db,
    IGitOperator<TRepo, TBranch, TCommitId> gitOperator,
    IVcsHostService vcsHostService)
where TCommitId : IEquatable<TCommitId>
{
    private readonly HighDb db = db;
    private readonly IGitOperator<TRepo, TBranch, TCommitId> gitOperator = gitOperator;
    private readonly IVcsHostService vcsHostService = vcsHostService;

    private string FormatCommitMessage(string fromBranch, string toBranch, int prNumber)
    {
        string pr = vcsHostService.formatPrNumber(prNumber);
        return $"Merge {fromBranch} into {toBranch} ({pr})";
    }

    private string FormatTemporaryBranchName(int prNumber)
    {
        return $"merge-{prNumber}";
    }

    public async Task OnRequestToAddToMergeQueue(string uri, int prNumber, CommitterInfo info)
    {
        var repo = await gitOperator.OpenAndUpdateAsync(uri);
        using var txn = await db.BeginTransaction();

        // Acquire data from database, so the transaction is open
        var dbRepo = await db.GetRepoByUrl(uri);
        var dbPr = await db.GetPrByRepoAndNumber(dbRepo.Id, prNumber);
        var dbMq = await db.GetMergeQueueById(dbPr.MergeQueueId);

        // Check add to mq criteria:
        // - It has passed its own merge CI
        // - It has no merge conflict with the target branch

        // Check for CI status
        var ciStatus = await vcsHostService.PullRequestCheckCIStatus(uri.ToString(), prNumber);
        switch (ciStatus)
        {
            case CIStatus.Passed:
                break;
            case CIStatus.Failed:
                throw new FailedToMergeException(FailedToMergeException.ReasonKind.CIFailed);
            case CIStatus.NotFinished:
                throw new FailedToMergeException(FailedToMergeException.ReasonKind.CIStillRunning);
        }

        // Check for merge conflict
        var sourceBranch = dbPr.SourceBranch;
        var workingBranch = dbMq.WorkingBranch;
        var gitSourceBranch = await gitOperator.GetBranchAsync(repo, sourceBranch)
            ?? throw new InvalidOperationException($"Branch {sourceBranch} not found in repo {uri}");
        var gitWorkingBranch = await gitOperator.GetBranchAsync(repo, workingBranch)
            ?? throw new InvalidOperationException($"Branch {workingBranch} not found in repo {uri}");
        var hasConflict = await gitOperator.CheckForMergeConflictAsync(gitWorkingBranch, gitSourceBranch);
        if (hasConflict)
        {
            throw new FailedToMergeException(FailedToMergeException.ReasonKind.MergeConflict);
        }

        // Create a temporary branch for the merge
        var branchName = FormatTemporaryBranchName(prNumber);
        var commitId = await gitOperator.GetBranchTipAsync(repo, gitSourceBranch);
        await gitOperator.CreateBranchAtCommitAsync(repo, branchName, commitId);
        var resultCommit = await gitOperator.MergeBranchesAsync(
            gitWorkingBranch,
            gitSourceBranch,
            FormatCommitMessage(sourceBranch, workingBranch, prNumber), info);
        if (resultCommit == null)
        {
            throw new FailedToMergeException(FailedToMergeException.ReasonKind.MergeConflict);
        }


    }

    private async Task DoAddToMergeQueue(PullRequest pr, MergeQueue mq)
    {
        // Check if the PR is already in the merge queue
        if (pr.MqSequenceNumber != null)
        {
            return;
        }

        // Check if the PR can be added to the tail of the merge queue

    }
}

public class FailedToMergeException(FailedToMergeException.ReasonKind reason) : Exception
{
    public enum ReasonKind
    {
        CIFailed,
        CIStillRunning,
        MergeConflict
    }

    public ReasonKind Reason { get; } = reason;
}
