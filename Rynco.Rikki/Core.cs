using Rynco.Rikki.Db;
using LibGit2Sharp;

namespace Rynco.Rikki;

public sealed class Core
{
    string rootPath;
    RikkiDbContext db;
    string commitName;

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

    private void DoMerge(Repository repo, Branch mergeSource, Branch mergeTarget, MergeStyle mergeStyle)
    {
        switch (mergeStyle)
        {
            case MergeStyle.Merge:
                {
                    var options = new MergeTreeOptions();
                    var res = repo.ObjectDatabase.MergeCommits(mergeSource.Tip, mergeTarget.Tip, options);
                    if (res.Status != MergeTreeStatus.Succeeded)
                    {
                        throw new Exception("Merge failed, conflict detected");
                    }
                    // Commit the merge.
                    var signature = new Signature(commitName, commitName, DateTimeOffset.Now);
                    var commit = repo.ObjectDatabase.MergeCommits(mergeSource.Tip, mergeTarget.Tip, options);
                    repo.Commit($"Merge {mergeSource.FriendlyName} into {mergeTarget.FriendlyName}", signature, signature);
                    break;
                }

        }
    }

    /// <summary>
    /// Callback to run when a pull request is added to a merge queue.
    /// </summary>
    /// <param name="repoUri"></param>
    /// <param name="prNumber"></param>
    /// <exception cref="ArgumentException"></exception>
    public void OnAddToMergeQueue(Uri repoUri, int prNumber)
    {
        var uriString = repoUri.ToString();
        using var txn = db.Database.BeginTransaction();

        var dbRepo = db.Repos.FirstOrDefault(r => r.Url == uriString)
            ?? throw new ArgumentException($"Repository {repoUri} not found in database");
        var dbPr = db.PullRequests.FirstOrDefault(pr => pr.RepoId == dbRepo.Id && pr.Number == prNumber)
            ?? throw new ArgumentException($"Pull request {prNumber} of repo {repoUri} not found in database");
        var dbMq = db.MergeQueues.FirstOrDefault(mq => mq.RepoId == dbRepo.Id)
            ?? throw new ArgumentException($"Merge queue for repo {repoUri} not found in database");

        var repo = OpenOrClone(repoUri);

        // Update the local repository with the latest changes.
        RepoPull(repo);

        // Get the branch of the pull request.
        var branchName = dbPr.SourceBranch;
        var branch = repo.Branches[branchName]
            ?? throw new ArgumentException($"Branch {branchName} not found in repository {repoUri}");
        var branchTip = branch.Tip;

        // Get the tip commit of the merge queue.
        var mergeQueueBranch = repo.Branches[dbMq.TargetBranch]
            ?? throw new ArgumentException($"Merge queue branch {dbMq.TargetBranch} not found in repository {repoUri}");
        var tip = mergeQueueBranch.Tip;

        // Try to merge the branch into the tip commit.
        if (!repo.ObjectDatabase.CanMergeWithoutConflict(branchTip, tip))
        {
            throw new ArgumentException($"Cannot merge branch {branchName} into merge queue for {repoUri}, conflict detected");
        }

    }
}
