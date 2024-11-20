using System.Net;
using Microsoft.EntityFrameworkCore;
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

    public async Task OnPrAdded(string uri, int prNumber, int priority, string sourceBranch, string targetBranch)
    {
        using var txn = await db.BeginTransaction();
        var dbRepo = await db.GetRepoByUrl(uri);
        var dbMq = await db.GetMergeQueueByRepoAndBranch(dbRepo.Id, targetBranch);
        if (dbMq == null)
        {
            return; // No merge queue was created for this branch, silently ignore
        }

        db.AddPr(new PullRequest
        {
            RepoId = dbRepo.Id,
            MergeQueueId = dbMq.Id,
            SourceBranch = sourceBranch,
            TargetBranch = targetBranch,
            Number = prNumber,
            Priority = priority
        });

        await db.SaveChanges();
        await txn.CommitAsync();
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

        // Try to add to merge queue
        await AddToMergeQueue(uri, repo, dbPr, dbMq, info, dbRepo.MergeStyle);

        // Succeeded? Now commit the changes
        await db.SaveChanges();
        await txn.CommitAsync();
    }

    /// <summary>
    /// Insert the given pull request into the appropriate place in the merge queue. This operation
    /// may optionally rebuild the merge queue to ensure the correct order.
    /// </summary>
    /// <param name="pr"></param>
    /// <param name="mq"></param>
    /// <returns></returns>
    private async Task AddToMergeQueue(
        string uri,
        TRepo repo,
        PullRequest pr,
        MergeQueue mq,
        CommitterInfo info,
        MergeStyle mergeStyle)
    {
        // Check if the PR is already in the merge queue
        if (pr.CiInfo != null)
        {
            throw new InvalidOperationException("The PR is already in the merge queue.");
        }

        // Check if the PR can be added to the tail of the merge queue
        var tailPr = await db.GetTailPrInMergeQueue(mq);
        var canAddToTail = tailPr == null || tailPr.Priority >= pr.Priority;

        if (canAddToTail)
        {
            // Simply add to the tail
            var commit = await PutPrToTailOfMergeQueue(uri, repo, pr, mq, info, mergeStyle);
            var tailSeqNum = mq.TailSequenceNumber;
            mq.TailSequenceNumber++;
            pr.CiInfo = new EnqueuedPullRequest
            {
                PullRequestId = pr.Id,
                SequenceNumber = tailSeqNum,
                AssociatedBranch = mq.WorkingBranch,
                MqCommit = gitOperator.FormatCommitId(commit)
            };
        }
        else
        {
            // No luck.
            // First we need to get all PRs in the merge queue, so we know where to insert the PR.
            var prs = await db.GetPrsInMergeQueue(mq);
            // prs should be a list of PRs in the merge queue, sorted by priority from high to low.
            // Now we see where to insert this PR.
            var insertPriority = pr.Priority;
            var insertIndex = prs.FindIndex(p => p.Priority < insertPriority);
            if (insertIndex == -1)
            {
                throw new Exception("Expected that the PR should be inserted somewhere in the middle of the merge queue, but found out it should be added to the tail.");
            }

            // Now we need to rebuild the merge queue from the PR at insertIndex.
            var prsToAdd = prs.Skip(insertIndex).Prepend(pr).ToArray();
            var rebuildAfter = insertIndex == 0 ? null : prs[insertIndex - 1];
            var rebuildResult = await RebuildMergeQueue(uri, repo, rebuildAfter, prsToAdd, mq, mergeStyle);
            // TODO: There's a list of failed PRs here. We should notify the user about them.

        }
    }

    /// <summary>
    /// Puts the given PR to the tail of the merge queue.
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="repo"></param>
    /// <param name="pr"></param>
    /// <param name="mq"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="FailedToMergeException"></exception>
    private async Task<TCommitId> PutPrToTailOfMergeQueue(
        string uri,
        TRepo repo,
        PullRequest pr,
        MergeQueue mq,
        CommitterInfo info,
        MergeStyle mergeStyle)
    {
        // Check for merge conflict
        var sourceBranch = pr.SourceBranch;
        var workingBranch = mq.WorkingBranch;
        var gitSourceBranch = await gitOperator.GetBranchAsync(repo, sourceBranch)
            ?? throw new InvalidOperationException($"Branch {sourceBranch} not found in repo {uri}");
        var gitWorkingBranch = await gitOperator.GetBranchAsync(repo, workingBranch)
            ?? throw new InvalidOperationException($"Branch {workingBranch} not found in repo {uri}");
        var noConflict = await gitOperator.CanMergeWithoutConflict(repo, gitWorkingBranch, gitSourceBranch);
        if (!noConflict)
        {
            throw new FailedToMergeException(FailedToMergeException.ReasonKind.MergeConflict);
        }

        // Create a temporary branch for the merge
        var tempBranchName = FormatTemporaryBranchName(pr.Number);
        var commitId = await gitOperator.GetBranchTipAsync(repo, gitSourceBranch);
        var tempBranch = await gitOperator.CreateBranchAtCommitAsync(repo, tempBranchName, commitId);
        var resultCommit = await gitOperator.PerformMergeAsync(
            repo,
            mergeStyle,
            gitWorkingBranch,
            tempBranch,
            FormatCommitMessage(sourceBranch, workingBranch, pr.Number),
            info);
        if (resultCommit == null)
        {
            throw new FailedToMergeException(FailedToMergeException.ReasonKind.MergeConflict);
        }

        // Remove the temporary branch and fast forward the working branch
        await gitOperator.ResetBranchToCommitAsync(repo, gitWorkingBranch, resultCommit);
        await gitOperator.RemoveBranchAsync(repo, tempBranch);

        return resultCommit;
    }

    /// <summary>
    /// Rebuild the portion of the merge queue after the given PR.
    /// 
    /// This method updates the git repository and database status, but doesn't sync the database.
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="repo"></param>
    /// <param name="rebuildAfter">
    /// The PR to build after, or null if the merge queue needs to be rebuilt from ground up
    /// </param>
    /// <param name="prsToAdd"></param>
    /// <param name="mq"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    private async Task<RebuildResult<TCommitId>> RebuildMergeQueue(
        string uri,
        TRepo repo,
        PullRequest? rebuildAfter,
        ReadOnlyMemory<PullRequest> prsToAdd,
        MergeQueue mq,
        MergeStyle mergeStyle)
    {
        TCommitId baseCommit;

        if (rebuildAfter != null)
        {
            var mqCommitSha = rebuildAfter?.CiInfo?.MqCommit
               ?? throw new InvalidOperationException("The PR to rebuild after doesn't have a merge commit SHA.");
            baseCommit = gitOperator.ParseCommitId(mqCommitSha);
        }
        else
        {
            var targetBranch = await gitOperator.GetBranchAsync(repo, mq.TargetBranch)
                ?? throw new InvalidOperationException($"Branch {mq.TargetBranch} not found in repo {uri}");
            baseCommit = await gitOperator.GetBranchTipAsync(repo, targetBranch);
        }

        // Reset the working branch to the base commit
        var workingBranch = await gitOperator.GetBranchAsync(repo, mq.WorkingBranch)
            ?? throw new InvalidOperationException($"Branch {mq.WorkingBranch} not found in repo {uri}");
        await gitOperator.ResetBranchToCommitAsync(repo, workingBranch, baseCommit);

        // Add PRs to the working branch
        int seqNum = rebuildAfter?.CiInfo?.SequenceNumber + 1 ?? mq.HeadSequenceNumber;
        var failedPrs = new List<PullRequest>();
        for (int i = 0; i < prsToAdd.Length; i++)
        {
            var pr = prsToAdd.Span[i];
            try
            {
                // Get the original commit message and committer info from the original merge commit
                var lastCommit = pr.CiInfo?.MqCommit
                    ?? throw new InvalidOperationException("The PR doesn't have a merge commit SHA.");
                var (commitMessage, commitInfo) = await gitOperator.GetCommitInfoAsync(repo, gitOperator.ParseCommitId(lastCommit));
                var sha = await PutPrToTailOfMergeQueue(uri, repo, pr, mq, commitInfo, mergeStyle);
                var ciInfo = new EnqueuedPullRequest
                {
                    PullRequestId = pr.Id,
                    SequenceNumber = seqNum,
                    AssociatedBranch = mq.WorkingBranch,
                    MqCommit = gitOperator.FormatCommitId(sha)
                };
                pr.CiInfo = ciInfo;
                seqNum++;
            }
            catch (FailedToMergeException)
            {
                failedPrs.Add(pr);
                RemovePrFromMergeQueueOps(pr, mq);
            }
        }

        return new RebuildResult<TCommitId>(failedPrs, baseCommit);
    }

    private void RemovePrFromMergeQueueOps(PullRequest pr, MergeQueue mq)
    {
        pr.CiInfo = null;
    }

    /// <summary>
    /// Called when a CI run is created.
    /// </summary>
    /// <returns></returns>
    public async Task OnCiCreate(string repo, int ciNumber, string associatedCommit)
    {
        using var txn = await db.BeginTransaction();
        var ciInfo = await db.DbContext.PullRequestCiInfos.Where(
            ci => ci.MqCommit == associatedCommit
        ).FirstOrDefaultAsync();
        if (ciInfo == null)
        {
            // This CI run is not associated with a PR in the merge queue.
            return;
        }

        ciInfo.CiNumber = ciNumber;
        ciInfo.Finished = false;

        await db.SaveChanges();
        await txn.CommitAsync();
    }

    public async Task OnCiFinish(string repo, int ciNumber, bool success)
    {
        using var txn = await db.BeginTransaction();
        var ciInfo = await db.DbContext.PullRequestCiInfos.Where(
            ci => ci.CiNumber == ciNumber
        ).FirstOrDefaultAsync();
        if (ciInfo == null)
        {
            // This CI run is not associated with a PR in the merge queue.
            return;
        }

        ciInfo.CiNumber = null;
        ciInfo.Finished = true;
        ciInfo.Passed = success;

        await db.SaveChanges();

        // If the PR is in the head and passed, try to dequeue it and later ones
        if (success)
        {
            var mq = await db.GetMergeQueueAssociatedWithCi(ciInfo.PullRequestId);
            await DequeueFinishedPrs(repo, mq);
        }
        else
        {
            // CI failed. Remove this PR from the merge queue, and rebuild the rest of the queue.
            var pr = await db.GetPrById(ciInfo.PullRequestId);
            var mq = await db.GetMergeQueueById(pr.MergeQueueId);
            var dbRepo = await db.GetRepoById(mq.RepoId);

            // Rebuild the merge queue
            var enqueuedPrs = await db.GetPrsInMergeQueue(mq);
            var prPos = enqueuedPrs.FindIndex(p => p.Id == pr.Id);
            var prsToRebuild = enqueuedPrs.Skip(prPos).ToArray();

            PullRequest? rebuildAfter = null;
            if (prPos > 0)
            {
                rebuildAfter = enqueuedPrs[prPos - 1];
            }
            var gitRepo = await gitOperator.OpenAndUpdateAsync(repo);
            var rebuildResult = await RebuildMergeQueue(repo, gitRepo, rebuildAfter, prsToRebuild, mq, dbRepo.MergeStyle);

            pr.CiInfo = null;
            db.DbContext.Remove(ciInfo);
        }

        await db.SaveChanges();
        await txn.CommitAsync();
    }

    /// <summary>
    /// Move finished PRs in the head of the merge queue to the target branch.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="mq"></param>
    /// <returns></returns>
    private async Task DequeueFinishedPrs(string repoUri, MergeQueue mq)
    {
        // Iteratively query the database for consecutive PRs in the merge queue head that passed CI
        var prs = await db.GetPrsInMergeQueue(mq);
        var mergeList = prs.TakeWhile(pr => pr.CiInfo != null && pr.CiInfo.Finished && pr.CiInfo.Passed).ToList();
        if (mergeList.Count == 0)
        {
            // TODO: Warn about no PRs to merge
            return;
        }

        var lastToMerge = mergeList.Last();
        var lastCommit = lastToMerge.CiInfo!.MqCommit; // safe: we have checked non-null above
        var seqNum = lastToMerge.CiInfo!.SequenceNumber;

        var repo = await gitOperator.OpenAndUpdateAsync(repoUri);
        var targetBranch = await gitOperator.GetBranchAsync(repo, mq.TargetBranch)
            ?? throw new InvalidOperationException($"Branch {mq.TargetBranch} not found in repo {repoUri}");
        var commitId = gitOperator.ParseCommitId(lastCommit);
        await gitOperator.ResetBranchToCommitAsync(repo, targetBranch, commitId);

        await gitOperator.PushBranchAsync(repo, targetBranch);

        // Update the merge queue
        mq.HeadSequenceNumber = seqNum + 1;
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

public record RebuildResult<TCommitId>(List<PullRequest> FailedPrs, TCommitId NewTailCommit);
