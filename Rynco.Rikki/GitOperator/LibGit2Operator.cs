using LibGit2Sharp;

namespace Rynco.Rikki.GitOperator;

public class LibGit2Operator : IGitOperator<Repository, Branch, ObjectId>
{
    readonly string rootPath;
    readonly LibGit2Sharp.Handlers.CredentialsHandler credentialsHandler;

    public LibGit2Operator(string rootPath, LibGit2Sharp.Handlers.CredentialsHandler credentialsHandler)
    {
        this.rootPath = rootPath;
        this.credentialsHandler = credentialsHandler;
    }

    string FormatPath(string repoUri)
    {
        var uri = new Uri(repoUri);
        var host = uri.Host;
        var path = uri.AbsolutePath;
        // Strip the leading slash and trailing extension.
        path = path.TrimStart('/');
        path = Path.ChangeExtension(path, null);

        // We want the path to have exactly two segments here.
        var parts = path.Split("/");
        string normalizedPath;
        if (parts.Length == 0)
        {
            normalizedPath = Path.Combine("_", "_");
        }
        else if (parts.Length == 1)
        {
            normalizedPath = Path.Combine("_", parts[0]);
        }
        else
        {
            var normPart0 = parts[0].StartsWith('_') ? parts[0] : ("_" + parts[0]);
            var normPart1 = string.Join("_", parts.Skip(1));
            normalizedPath = Path.Combine(normPart0, normPart1);
        }

        return Path.Combine(rootPath, host, normalizedPath);
    }

    public ValueTask<bool> CanMergeWithoutConflict(Repository repo, Branch targetBranch, Branch sourceBranch)
    {
        return new ValueTask<bool>(Task.Run(() =>
        {
            return repo.ObjectDatabase.CanMergeWithoutConflict(targetBranch.Tip, sourceBranch.Tip);
        }));
    }

    public async ValueTask<Branch> CreateBranchAtCommit(Repository repo, string branchName, ObjectId commitId, bool overwriteExisting = false)
    {
        return await Task.Run(() =>
        {
            var commit = repo.Lookup<Commit>(commitId);
            if (repo.Branches[branchName] != null)
            {
                if (overwriteExisting)
                {
                    repo.Branches.Remove(branchName);
                }
                else
                {
                    throw new InvalidOperationException($"Branch {branchName} already exists.");
                }
            }
            return repo.CreateBranch(branchName, commit);
        });
    }

    public string FormatCommitId(ObjectId commitId)
    {
        return commitId.Sha;
    }

    public ObjectId ParseCommitId(string commitId)
    {
        return new ObjectId(commitId);
    }

    public ValueTask<Branch?> GetBranch(Repository repo, string branchName)
    {
        return new ValueTask<Branch?>(Task.Run(() =>
        {
            return (Branch?)repo.Branches[branchName]; // This call might return null
        }));
    }

    public ValueTask<ObjectId> GetBranchTip(Repository repo, Branch branch)
    {
        return ValueTask.FromResult(branch.Tip.Id);
    }

    public ValueTask<(string, CommitterInfo)> GetCommitInfo(Repository repo, ObjectId commitId)
    {
        return new ValueTask<(string, CommitterInfo)>(Task.Run(() =>
        {
            var commit = repo.Lookup<Commit>(commitId);
            return (commit.Message, new CommitterInfo(commit.Committer.Name, commit.Committer.Email));
        }));
    }

    public ValueTask<ObjectId?> MergeBranches(Repository repo, Branch targetBranch, Branch sourceBranch, string commitMessage, CommitterInfo committerInfo)
    {
        return new ValueTask<ObjectId?>(Task.Run(() =>
        {
            var signature = new Signature(committerInfo.Name, committerInfo.Email, DateTimeOffset.Now);
            var result = repo.ObjectDatabase.MergeCommits(sourceBranch.Tip, targetBranch.Tip, new MergeTreeOptions
            {
                FailOnConflict = true
            });
            if (result.Status == MergeTreeStatus.Conflicts)
            {
                return null;
            }

            var commit = repo.ObjectDatabase.CreateCommit(signature, signature, commitMessage, result.Tree, [targetBranch.Tip, sourceBranch.Tip], true);

            return commit.Id;
        }));
    }

    public ValueTask<Repository> OpenAndUpdate(string uri)
    {
        var path = FormatPath(uri);
        return new ValueTask<Repository>(Task.Run(() =>
        {
            Repository repo;
            if (Path.Exists(path))
            {
                repo = new Repository(path);
            }
            else
            {
                // Create the directory if it doesn't exist.
                Directory.CreateDirectory(path);
                // Create a bare repository.
                repo = new Repository(Repository.Init(path, true));
                // Add remote 'origin' with the given uri.
                repo.Network.Remotes.Add("origin", uri);
            }
            // Pull from the remote.
            var origin = repo.Network.Remotes["origin"];
            var refspecs = origin.FetchRefSpecs.Select(r => r.Specification);
            repo.Network.Fetch("origin", refspecs, new FetchOptions
            {
                CredentialsProvider = credentialsHandler
            });
            return repo;
        }));
    }

    public ValueTask ForcePushBranch(Repository repo, Branch branch)
    {
        return new ValueTask(Task.Run(() =>
        {
            var remote = repo.Network.Remotes["origin"];
            repo.Network.Push(remote, branch.CanonicalName, new PushOptions
            {
                CredentialsProvider = credentialsHandler
            });
        }));
    }

    public ValueTask PushRepoState(Repository repo)
    {
        // aka git push --mirror
        return new ValueTask(Task.Run(() =>
        {
            var remote = repo.Network.Remotes["origin"];
            var refspecs = remote.PushRefSpecs.Select(r => r.Specification);
            repo.Network.Push(remote, refspecs, new PushOptions
            {
                CredentialsProvider = credentialsHandler
            });
        }));
    }

    public ValueTask<ObjectId?> RebaseBranches(Repository repo, Branch targetBranch, Branch sourceBranch, CommitterInfo committerInfo)
    {
        return new ValueTask<ObjectId?>(Task.Run(() =>
        {
            var identity = new Identity(committerInfo.Name, committerInfo.Email);
            var result = repo.Rebase.Start(
                sourceBranch,
                targetBranch,
                targetBranch,
                identity,
                new RebaseOptions());
            if (result.Status == RebaseStatus.Complete)
            {
                return sourceBranch.Tip.Id;
            }
            else
            {
                repo.Rebase.Abort();
                return null;
            }
        }));
    }

    public ValueTask RemoveBranchFromRemote(Repository repo, Branch branch)
    {
        return new ValueTask(Task.Run(() =>
        {
            repo.Branches.Remove(branch);
        }));
    }

    public ValueTask ResetBranchToCommit(Repository repo, Branch branch, ObjectId commitId)
    {
        return new ValueTask(Task.Run(() =>
        {
            repo.Refs.UpdateTarget(branch.Reference, commitId);
        }));
    }
}
