using LibGit2Sharp;

namespace Rynco.Rikki.GitOperator;

public class LibGit2Operator : IGitOperator<Repository, Branch, ObjectId>
{
    public ValueTask<bool> CheckForMergeConflictAsync(Repository repo, Branch targetBranch, Branch sourceBranch)
    {
        throw new NotImplementedException();
    }

    public ValueTask<Branch> CreateBranchAtCommitAsync(Repository repo, string branchName, ObjectId commitId)
    {
        throw new NotImplementedException();
    }

    public string FormatCommitId(ObjectId commitId)
    {
        throw new NotImplementedException();
    }

    public ValueTask<Branch?> GetBranchAsync(Repository repo, string branchName)
    {
        throw new NotImplementedException();
    }

    public ValueTask<ObjectId> GetBranchTipAsync(Repository repo, Branch branch)
    {
        throw new NotImplementedException();
    }

    public ValueTask<ObjectId?> MergeBranchesAsync(Repository repo, Branch targetBranch, Branch sourceBranch, string commitMessage, CommitterInfo committerInfo)
    {
        throw new NotImplementedException();
    }

    public ValueTask<Repository> OpenAndUpdateAsync(string uri)
    {
        throw new NotImplementedException();
    }

    public ObjectId ParseCommitId(string commitId)
    {
        throw new NotImplementedException();
    }

    public ValueTask PushBranchAsync(Repository repo, Branch branch)
    {
        throw new NotImplementedException();
    }

    public ValueTask PushRepoStateAsync(Repository repo)
    {
        throw new NotImplementedException();
    }

    public ValueTask<ObjectId?> RebaseBranchesAsync(Repository repo, Branch targetBranch, Branch sourceBranch)
    {
        throw new NotImplementedException();
    }

    public ValueTask RemoveBranchAsync(Repository repo, Branch branch)
    {
        throw new NotImplementedException();
    }

    public ValueTask ResetBranchToCommitAsync(Repository repo, Branch branch, ObjectId commitId)
    {
        throw new NotImplementedException();
    }
}
