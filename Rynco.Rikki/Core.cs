using Rynco.Rikki.Db;
using LibGit2Sharp;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Rynco.Rikki;

/// <summary>
/// The core machinery of Rikki. This class is responsible for handling callbacks triggered
/// by outside events.
/// </summary>
public sealed class Core
{
    string rootPath;
    RikkiDbContext db;
    string commitName;
    LibGit2Sharp.Handlers.CredentialsHandler credentialsHandler;

    public Core(string rootPath, RikkiDbContext db)
    {
        this.rootPath = rootPath;
        this.db = db;
    }

    private string RepoPathFromUri(Uri uri)
    {
        var host = uri.Host ?? "_no_host";
        var path = uri.AbsolutePath.TrimStart('/').Replace("/", "_");
        return Path.Combine(rootPath, host, path);
    }

    private Repository OpenOrClone(Uri uri)
    {
        var repoPath = RepoPathFromUri(uri);
        if (Directory.Exists(repoPath))
        {
            return new Repository(repoPath);
        }
        else
        {
            var path = Repository.Clone(uri.ToString(), repoPath, new CloneOptions
            {
                IsBare = true
            });
            return new Repository(path);
        }
    }

    private void RepoPull(Repository repo)
    {
        var remote = repo.Network.Remotes["origin"];
        var refSpecs = remote.FetchRefSpecs.Select(rs => rs.Specification);
        Commands.Fetch(repo, remote.Name, refSpecs, null, "");
    }

    private Repository OpenAndPull(Uri uri)
    {
        var repo = OpenOrClone(uri);
        RepoPull(repo);
        return repo;
    }

    private int PositionInMergeQueue(PullRequest pr, MergeQueue mq)
    {
        if (pr.MqSequenceNumber == null)
        {
            return -1;
        }
        else
        {
            return pr.MqSequenceNumber.Value - mq.HeadSequenceNumber;
        }
    }

    /// <summary>
    /// Merge the source branch onto the target branch using the given style. Updates the target
    /// branch to the merge commit.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="mergeSource"></param>
    /// <param name="mergeTarget"></param>
    /// <param name="mergeStyle"></param>
    /// <param name="signature">Which committer should we use?</param>
    /// <param name="commitMessage">Commit message, if used</param>
    /// <returns>The merge commit</returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentException"></exception>
    private Commit DoMerge(
        Repository repo,
        Branch mergeSource,
        Branch mergeTarget,
        MergeStyle mergeStyle,
        Signature signature,
        string commitMessage)
    {
        switch (mergeStyle)
        {
            case MergeStyle.Merge:
                // Merge using merge commits.
                {
                    var options = new MergeTreeOptions();
                    var res = repo.ObjectDatabase.MergeCommits(mergeSource.Tip, mergeTarget.Tip, options);
                    if (res.Status != MergeTreeStatus.Succeeded)
                    {
                        throw new Exception("Merge failed, conflict detected");
                    }
                    // Commit the merge.
                    var tree = res.Tree;
                    var commit = repo.ObjectDatabase.CreateCommit(
                           signature,
                           signature,
                           commitMessage,
                           tree,
                           [mergeSource.Tip, mergeTarget.Tip],
                           true);
                    repo.Refs.UpdateTarget(mergeTarget.Reference, commit.Id);
                    return commit;
                }

            case MergeStyle.SemiLinear:
                // First rebase the source branch onto the target branch, and then add a merge commit
                {
                    // Create a temporary branch that does the actual rebase
                    var rebaseBranch = repo.Branches.Add($"__rikki_rebase_{mergeSource.FriendlyName}", mergeSource.Tip);
                    var rebaseResult = repo.Rebase.Start(
                        rebaseBranch,
                        mergeTarget,
                        null,
                        new Identity(signature.Name, signature.Email),
                        new RebaseOptions());
                    if (rebaseResult.Status != RebaseStatus.Complete)
                    {
                        // Remove the temporary branch
                        repo.Branches.Remove(rebaseBranch);
                        throw new Exception("Rebase failed");
                    }
                    // Remove the temporary branch
                    repo.Branches.Remove(rebaseBranch);

                    // Add a merge commit
                    var rebaseCommit = rebaseBranch.Tip;
                    var mergeCommit = repo.ObjectDatabase.MergeCommits(rebaseCommit, mergeTarget.Tip, new MergeTreeOptions());
                    if (mergeCommit.Status != MergeTreeStatus.Succeeded)
                    {
                        throw new Exception("Merge failed, conflict detected");
                    }
                    var tree = mergeCommit.Tree;
                    var commit = repo.ObjectDatabase.CreateCommit(
                        signature,
                        signature,
                        commitMessage,
                        tree,
                        [rebaseCommit, mergeTarget.Tip],
                        true);
                    repo.Refs.UpdateTarget(mergeTarget.Reference, commit.Id);
                    return commit;
                }

            case MergeStyle.Linear:
                // Rebase the source branch onto the target branch
                {
                    var rebaseBranch = repo.Branches.Add($"__rikki_rebase_{mergeSource.FriendlyName}", mergeSource.Tip);
                    var rebaseResult = repo.Rebase.Start(
                        rebaseBranch,
                        mergeTarget,
                        null,
                        new Identity(signature.Name, signature.Email),
                        new RebaseOptions());
                    if (rebaseResult.Status != RebaseStatus.Complete)
                    {
                        // Remove the temporary branch
                        repo.Branches.Remove(rebaseBranch);
                        throw new Exception("Rebase failed");
                    }
                    var rebaseCommit = rebaseBranch.Tip;
                    // Update merge target to the rebased commit
                    repo.Refs.UpdateTarget(mergeTarget.Reference, rebaseCommit.Id);
                    // Remove the temporary branch
                    repo.Branches.Remove(rebaseBranch);
                    return rebaseCommit;
                }
            default:
                throw new ArgumentException("Invalid merge style");
        }
    }

    /// <summary>
    /// Callback to run when a pull request is added to a merge queue.
    /// </summary>
    /// <param name="repoUri"></param>
    /// <param name="prNumber"></param>
    /// <exception cref="ArgumentException"></exception>
    public async void OnAddToMergeQueue(Uri repoUri, int prNumber, Signature committer)
    {
        var uriString = repoUri.ToString();
        using var txn = db.Database.BeginTransaction();

        var dbRepo = await db.Repos.FirstOrDefaultAsync(r => r.Url == uriString)
            ?? throw new ArgumentException($"Repository {repoUri} not found in database");
        var dbPr = await db.PullRequests.FirstOrDefaultAsync(pr => pr.RepoId == dbRepo.Id && pr.Number == prNumber)
            ?? throw new ArgumentException($"Pull request {prNumber} of repo {repoUri} not found in database");
        var dbMq = await db.MergeQueues.FirstOrDefaultAsync(mq => mq.RepoId == dbRepo.Id)
            ?? throw new ArgumentException($"Merge queue for repo {repoUri} not found in database");

        // Check if we need to rebuild the merge queue. It happens when the PR has a 
        // priority higher than the current head of the merge queue, so it can't be inserted
        // at the head of the queue.
        var currentHead = db.PullRequests.FirstOrDefault(pr =>
            pr.MergeQueueId == dbMq.Id
            && pr.MqSequenceNumber == dbMq.TailSequenceNumber);
        var needRebuild = currentHead != null && dbPr.Priority > currentHead.Priority;

        if (needRebuild)
        {
            var prs = await GetMqEnqueuedPrs(dbMq);
            await Task.Run(() =>
            {
                var repo = OpenAndPull(repoUri);
                var failedPrs = RebuildMergeQueue(repo, dbMq, prs, committer);
                foreach (var failedPr in failedPrs)
                {
                    failedPr.MqSequenceNumber = null;
                    failedPr.MqCommitSha = null;
                    failedPr.MqCiId = null;
                }
            });

            // Update the database.
            await db.SaveChangesAsync();
            await txn.CommitAsync();
        }
        else
        {
            var resultCommit = await Task.Run(() =>
            {
                var repo = OpenAndPull(repoUri);


                // Get the branch of the pull request.
                var branchName = dbPr.SourceBranch;
                var branch = repo.Branches[branchName]
                    ?? throw new ArgumentException($"Branch {branchName} not found in repository {repoUri}");
                var branchTip = branch.Tip;

                // Get the tip commit of the merge queue.
                var mergeQueueBranch = repo.Branches[dbMq.WorkingBranch]
                    ?? throw new ArgumentException($"Merge queue branch {dbMq.TargetBranch} not found in repository {repoUri}");
                var tip = mergeQueueBranch.Tip;

                // Try to merge the branch into the tip commit.
                if (!repo.ObjectDatabase.CanMergeWithoutConflict(branchTip, tip))
                {
                    throw new ArgumentException($"Cannot merge branch {branchName} into merge queue for {repoUri}, conflict detected");
                }

                var resultCommit = DoMerge(
                    repo,
                    branch,
                    mergeQueueBranch,
                    MergeStyle.Merge,
                    committer,
                    $"Merge pull request #{prNumber} from {branchName}");

                // Push the merge commit to the remote repository.
                var remote = repo.Network.Remotes["origin"];
                repo.Network.Push(mergeQueueBranch, new PushOptions
                {
                    CredentialsProvider = this.credentialsHandler
                });

                return resultCommit;
            });

            // Update the database.
            dbMq.TipCommit = resultCommit.Sha;
            dbPr.MqCommitSha = resultCommit.Sha;
        }
        await db.SaveChangesAsync();
        await txn.CommitAsync();
    }

    public async void OnMergeQueueCIStarted(Uri repoUri, string commitSha, int ciId)
    {
        var uriString = repoUri.ToString();
        using var txn = db.Database.BeginTransaction();

        var dbRepo = await db.Repos.FirstOrDefaultAsync(r => r.Url == uriString)
            ?? throw new ArgumentException($"Repository {repoUri} not found in database");
        var dbPr = await db.PullRequests.FirstOrDefaultAsync(pr => pr.MqCommitSha == commitSha)
            ?? throw new ArgumentException($"Pull request with merge queue commit {commitSha} not found in database");

        dbPr.MqCiId = ciId;

        await db.SaveChangesAsync();
        await txn.CommitAsync();
    }

    /// <summary>
    /// Rebuild the merge queue branch for the given repository, given the list of pull requests.
    /// This method does not commit the changes to the database.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="queue"></param>
    /// <param name="prs">The pull requests, sorted in desired merge order</param>
    /// <returns>The list of pull requests that failed to merge</returns>
    public List<PullRequest> RebuildMergeQueue(Repository repo, MergeQueue queue, List<PullRequest> prs, Signature committer)
    {
        int seqNum = queue.HeadSequenceNumber;
        var targetBranch = repo.Branches[queue.TargetBranch]
            ?? throw new ArgumentException($"Merge queue branch {queue.TargetBranch} not found in repository {repo.Info.WorkingDirectory}");

        // Reset the working branch to the tip of the target branch.
        var workingBranch = repo.Branches[queue.WorkingBranch]
            ?? throw new ArgumentException($"Merge queue working branch {queue.WorkingBranch} not found in repository {repo.Info.WorkingDirectory}");
        repo.Refs.UpdateTarget(workingBranch.Reference, targetBranch.Tip.Id);

        // Merge the pull requests in order.
        var failedPrs = new List<PullRequest>();
        foreach (var pr in prs)
        {
            var branch = repo.Branches[pr.SourceBranch]
                ?? throw new ArgumentException($"Branch {pr.SourceBranch} not found in repository {repo.Info.WorkingDirectory}");
            var branchTip = branch.Tip;

            if (!repo.ObjectDatabase.CanMergeWithoutConflict(branchTip, targetBranch.Tip))
            {
                failedPrs.Add(pr);
                continue;
            }

            var commit = DoMerge(
                repo,
                branch,
                targetBranch,
                MergeStyle.Merge,
                committer,
                $"Merge pull request #{pr.Number} from {pr.SourceBranch}");

            // Update the database.
            pr.MqSequenceNumber = seqNum++;
            pr.MqCommitSha = commit.Sha;
        }

        // Update the database.
        queue.HeadSequenceNumber = seqNum;

        // Push the changes to the remote repository.
        repo.Network.Push(workingBranch, new PushOptions
        {
            CredentialsProvider = this.credentialsHandler
        });

        return failedPrs;
    }

    private async Task<List<PullRequest>> GetMqEnqueuedPrs(MergeQueue mq)
    {
        return await db.PullRequests.Where(pr =>
            pr.MergeQueueId == mq.Id
            && pr.MqSequenceNumber != null
            && pr.MqSequenceNumber >= mq.HeadSequenceNumber).ToListAsync();
    }

    public async void OnMergeQueueCICompleted(Uri repoUri, string commitSha, bool success, Signature committer)
    {
        var uriString = repoUri.ToString();
        using var txn = db.Database.BeginTransaction();

        var dbRepo = await db.Repos.FirstOrDefaultAsync(r => r.Url == uriString)
            ?? throw new ArgumentException($"Repository {repoUri} not found in database");
        var dbPr = await db.PullRequests.FirstOrDefaultAsync(pr => pr.MqCommitSha == commitSha)
            ?? throw new ArgumentException($"Pull request with merge queue commit {commitSha} not found in database");
        var dbMq = await db.MergeQueues.FirstOrDefaultAsync(mq => mq.Id == dbPr.MergeQueueId)
            ?? throw new ArgumentException($"Merge queue {dbPr.MergeQueueId} not found in database");

        var repo = await Task.Run(() => OpenAndPull(repoUri));

        // If CI succeeded and the PR is the first in the queue, merge it and any subsequent PRs 
        // that have passed CI.
        if (success)
        {
            if (dbPr.MqSequenceNumber == dbMq.HeadSequenceNumber)
            {
                PullRequest mergeHead = dbPr;
                int seq = dbMq.HeadSequenceNumber + 1;
                while (true)
                {
                    var nextPr = db.PullRequests.FirstOrDefault(pr =>
                        pr.MergeQueueId == dbMq.Id
                        && pr.MqSequenceNumber == seq);
                    if (nextPr == null || !nextPr.MqCiPassed)
                    {
                        // No more PRs to merge.
                        break;
                    }

                    mergeHead = nextPr;
                    seq++;
                }

                await Task.Run(() =>
                {
                    var targetBranch = repo.Branches[dbMq.TargetBranch]
                        ?? throw new ArgumentException($"Merge queue branch {dbMq.TargetBranch} not found in repository {repo.Info.WorkingDirectory}");
                    repo.Refs.UpdateTarget(targetBranch.Reference, mergeHead.MqCommitSha);

                    // Push the changes to the remote repository.
                    repo.Network.Push(targetBranch, new PushOptions
                    {
                        CredentialsProvider = this.credentialsHandler
                    });
                });
            }
            else
            {
                // The PR is not the first in the queue, just update the database so it can be
                // merged once the previous PRs have passed CI.
                dbPr.MqCiPassed = true;
            }
        }
        else
        {
            // It didn't pass CI, remove it and rebuild the merge queue.
            dbPr.MqCiPassed = false;
            dbPr.MqCiId = null;
            dbPr.MqSequenceNumber = null;

            await db.SaveChangesAsync();

            // Now the enqueued PRs don't contain the failed PR.
            var prs = await GetMqEnqueuedPrs(dbMq);

            await Task.Run(() =>
            {
                var failedPrs = RebuildMergeQueue(repo, dbMq, prs, committer);
                foreach (var failedPr in failedPrs)
                {
                    failedPr.MqSequenceNumber = null;
                    failedPr.MqCommitSha = null;
                    failedPr.MqCiId = null;
                }

                // Push the changes to the remote repository.
                var targetBranch = repo.Branches[dbMq.WorkingBranch]
                    ?? throw new ArgumentException($"Merge queue working branch {dbMq.WorkingBranch} not found in repository {repo.Info.WorkingDirectory}");
                repo.Network.Push(targetBranch, new PushOptions
                {
                    CredentialsProvider = this.credentialsHandler
                });
            });
        }

        await db.SaveChangesAsync();
        await txn.CommitAsync();
    }
}
