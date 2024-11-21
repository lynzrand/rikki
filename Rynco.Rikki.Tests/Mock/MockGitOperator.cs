using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Constraints;
using Rynco.Rikki.GitOperator;

namespace Rynco.Rikki.Tests;

/// <summary>
/// A simple mock implementation of <see cref="IGitOperator{TRepo, TBranch, TCommitId}"/>.
/// </summary>
public class MockGitOperator : IGitOperator<MockRepo, MockBranch, MockCommitId>
{
    ILogger<MockGitOperator>? logger;

    public MockGitOperator(ILogger<MockGitOperator>? logger = null)
    {
        this.logger = logger;
    }

    private Dictionary<string, MockRepo> repos = [];

    public MockRepo CreateRepository(string uri)
    {
        var repo = new MockRepo(uri);
        repos.Add(uri, repo);
        return repo;
    }

    public string FormatCommitId(MockCommitId commitId)
    {
        return commitId.Id.ToString();
    }

    public MockCommitId ParseCommitId(string commitId)
    {
        return new MockCommitId(int.Parse(commitId));
    }

    public ValueTask<bool> CanMergeWithoutConflict(MockRepo repo, MockBranch targetBranch, MockBranch sourceBranch)
    {
        // get result safety: no async in the method, so no blocking
        var targetCommit = GetBranchTip(repo, targetBranch).Result;
        var sourceCommit = GetBranchTip(repo, sourceBranch).Result;
        var mergedTree = repo.TryMergeTrees(targetCommit, sourceCommit);
        return new ValueTask<bool>(mergedTree != null);
    }

    public ValueTask<MockBranch> CreateBranchAtCommit(MockRepo repo, string branchName, MockCommitId commitId, bool overwriteExisting = false)
    {
        if (repo.Branches.ContainsKey(branchName))
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
        repo.CreateBranch(branchName, commitId);
        return new ValueTask<MockBranch>(new MockBranch(branchName));
    }

    public ValueTask<MockBranch?> GetBranch(MockRepo repo, string branchName)
    {
        return new ValueTask<MockBranch?>(repo.GetBranch(branchName));
    }

    public ValueTask<MockCommitId> GetBranchTip(MockRepo repo, MockBranch branch)
    {
        return new ValueTask<MockCommitId>(new MockCommitId(repo.Branches[branch.Name]));
    }

    public ValueTask<MockCommitId?> MergeBranches(MockRepo repo, MockBranch targetBranch, MockBranch sourceBranch, string commitMessage, CommitterInfo committerInfo)
    {
        var targetCommit = GetBranchTip(repo, targetBranch).Result;
        var sourceCommit = GetBranchTip(repo, sourceBranch).Result;
        var mergedTree = repo.TryMergeTrees(targetCommit, sourceCommit);
        if (mergedTree == null)
        {
            return ValueTask.FromResult<MockCommitId?>(null);
        }

        var newCommit = repo.CreateCommit(new MockCommit(commitMessage, committerInfo, new() { targetCommit.Id, sourceCommit.Id }, mergedTree));
        return new ValueTask<MockCommitId?>(new MockCommitId(newCommit.Id));
    }

    public ValueTask<MockRepo> OpenAndUpdate(string uri)
    {
        if (repos.TryGetValue(uri, out var repo))
        {
            return new ValueTask<MockRepo>(repo);
        }
        throw new Exception("Repo not found");
    }

    public ValueTask ForcePushBranch(MockRepo repo, MockBranch branch)
    {
        // noop
        return default;
    }

    public ValueTask PushRepoState(MockRepo repo)
    {
        // noop
        return default;
    }

    public ValueTask<MockCommitId?> RebaseBranches(MockRepo repo, MockBranch targetBranch, MockBranch sourceBranch, CommitterInfo committerInfo)
    {
        var targetCommit = GetBranchTip(repo, targetBranch).Result;
        var sourceCommit = GetBranchTip(repo, sourceBranch).Result;
        var baseCommit = repo.FindLCAs(targetCommit, sourceCommit).First();
        var newCommit = repo.TryRebaseCommits(targetCommit, sourceCommit, baseCommit);
        if (newCommit == null)
        {
            return ValueTask.FromResult<MockCommitId?>(null);
        }
        return new ValueTask<MockCommitId?>(new MockCommitId(newCommit.Id));
    }

    public ValueTask RemoveBranchFromRemote(MockRepo repo, MockBranch branch)
    {
        repo.RemoveBranch(branch.Name);
        return default;
    }

    public ValueTask ResetBranchToCommit(MockRepo repo, MockBranch branch, MockCommitId commitId)
    {
        repo.Branches[branch.Name] = commitId.Id;
        return default;
    }

    public ValueTask<(string, CommitterInfo)> GetCommitInfo(MockRepo repo, MockCommitId commitId)
    {
        var commit = repo.GetCommit(commitId.Id);
        return new ValueTask<(string, CommitterInfo)>((commit.Message, commit.CommitterInfo));
    }


}
