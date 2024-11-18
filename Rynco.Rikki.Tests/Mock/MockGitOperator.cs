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

    public ValueTask<bool> CheckForMergeConflictAsync(MockRepo repo, MockBranch targetBranch, MockBranch sourceBranch)
    {
        // get result safety: no async in the method, so no blocking
        var targetCommit = GetBranchTipAsync(repo, targetBranch).Result;
        var sourceCommit = GetBranchTipAsync(repo, sourceBranch).Result;
        var mergedTree = repo.TryMergeTrees(targetCommit, sourceCommit);
        return new ValueTask<bool>(mergedTree == null);
    }

    public ValueTask<MockBranch> CreateBranchAtCommitAsync(MockRepo repo, string branchName, MockCommitId commitId)
    {
        repo.CreateBranch(branchName, commitId);
        return new ValueTask<MockBranch>(new MockBranch(branchName));
    }

    public ValueTask<MockBranch?> GetBranchAsync(MockRepo repo, string branchName)
    {
        return new ValueTask<MockBranch?>(repo.GetBranch(branchName));
    }

    public ValueTask<MockCommitId> GetBranchTipAsync(MockRepo repo, MockBranch branch)
    {
        return new ValueTask<MockCommitId>(new MockCommitId(repo.Branches[branch.Name]));
    }

    public ValueTask<MockCommitId?> MergeBranchesAsync(MockRepo repo, MockBranch targetBranch, MockBranch sourceBranch, string commitMessage, CommitterInfo committerInfo)
    {
        var targetCommit = GetBranchTipAsync(repo, targetBranch).Result;
        var sourceCommit = GetBranchTipAsync(repo, sourceBranch).Result;
        var mergedTree = repo.TryMergeTrees(targetCommit, sourceCommit);
        if (mergedTree == null)
        {
            return ValueTask.FromResult<MockCommitId?>(null);
        }

        var newCommit = repo.CreateCommit(new MockCommit(commitMessage, committerInfo, new() { targetCommit.Id, sourceCommit.Id }, mergedTree));
        return new ValueTask<MockCommitId?>(new MockCommitId(newCommit.Id));
    }

    public ValueTask<MockRepo> OpenAndUpdateAsync(string uri)
    {
        if (repos.TryGetValue(uri, out var repo))
        {
            return new ValueTask<MockRepo>(repo);
        }
        throw new Exception("Repo not found");
    }

    public ValueTask PushBranchAsync(MockRepo repo, MockBranch branch)
    {
        // noop
        return default;
    }

    public ValueTask PushRepoStateAsync(MockRepo repo)
    {
        // noop
        return default;
    }

    public ValueTask<MockCommitId?> RebaseBranchesAsync(MockRepo repo, MockBranch targetBranch, MockBranch sourceBranch)
    {
        var targetCommit = GetBranchTipAsync(repo, targetBranch).Result;
        var sourceCommit = GetBranchTipAsync(repo, sourceBranch).Result;
        var baseCommit = repo.FindLCAs(targetCommit, sourceCommit).First();
        var newCommit = repo.TryRebaseCommits(targetCommit, sourceCommit, baseCommit);
        if (newCommit == null)
        {
            return ValueTask.FromResult<MockCommitId?>(null);
        }
        return new ValueTask<MockCommitId?>(new MockCommitId(newCommit.Id));
    }

    public ValueTask RemoveBranchAsync(MockRepo repo, MockBranch branch)
    {
        repo.RemoveBranch(branch.Name);
        return default;
    }

    public ValueTask ResetBranchToCommitAsync(MockRepo repo, MockBranch branch, MockCommitId commitId)
    {
        repo.Branches[branch.Name] = commitId.Id;
        return default;
    }

    public ValueTask<(string, CommitterInfo)> GetCommitInfoAsync(MockRepo repo, MockCommitId commitId)
    {
        var commit = repo.GetCommit(commitId.Id);
        return new ValueTask<(string, CommitterInfo)>((commit.Message, commit.CommitterInfo));
    }
}
