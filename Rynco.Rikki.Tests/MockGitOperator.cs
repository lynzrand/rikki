using System.Collections.Immutable;
using Rynco.Rikki.GitOperator;

namespace Rynco.Rikki.Tests;

/// <summary>
/// A mock repository for testing purposes. Commit ids are integers, and branches are strings.
/// Conflict check is based on intersection of changed files list; content is not checked.
/// </summary>
/// <param name="name"></param>
public class MockRepo(string name)
{
    readonly string name = name;
    int maxCommitId = 0;

    internal Dictionary<string, int> Branches { get; } = [];
    internal Dictionary<int, MockCommit> Commits { get; } = [];

    public string Name => name;

    public MockCommitId CreateCommit(MockCommit info)
    {
        maxCommitId++;
        Commits.Add(maxCommitId, info);
        return new MockCommitId(maxCommitId);
    }

    public void CreateBranch(string branchName, MockCommitId commitId)
    {
        Branches.Add(branchName, commitId.Id);
    }

    public void RemoveBranch(string branchName)
    {
        Branches.Remove(branchName);
    }

    public MockBranch? GetBranch(string branchName)
    {
        if (Branches.TryGetValue(branchName, out var commitId))
        {
            return new MockBranch(branchName);
        }
        return null;
    }

    public List<MockCommitId> FindLCAs(MockCommitId commitA, MockCommitId commitB)
    {
        var commitA_Ancestors = new HashSet<int>();
        var commitB_Ancestors = new HashSet<int>();

        void dfs(int commitId, HashSet<int> ancestors)
        {
            if (ancestors.Contains(commitId))
            {
                return;
            }
            ancestors.Add(commitId);
            foreach (var parentId in Commits[commitId].ParentIds)
            {
                dfs(parentId, ancestors);
            }
        }

        dfs(commitA.Id, commitA_Ancestors);
        dfs(commitB.Id, commitB_Ancestors);

        var commonAncestors = commitA_Ancestors.Intersect(commitB_Ancestors).ToList();
        var commonAncestorsDict = commonAncestors.ToDictionary(id => id, id => 0);
        // Calculate the out degree of each common ancestor.
        foreach (var ancestor in commonAncestors)
        {
            var commit = Commits[ancestor];
            foreach (var parentId in commit.ParentIds)
            {
                if (commonAncestorsDict.ContainsKey(parentId))
                {
                    commonAncestorsDict[parentId]++;
                }
            }
        }

        // LCA list is the common ancestors with out degree 0.
        return commonAncestors.Where(id => commonAncestorsDict[id] == 0).Select(id => new MockCommitId(id)).ToList();
    }

    /// <summary>
    /// Diff two mocked trees. Returns the set of changed files.
    /// </summary>
    /// <param name="treeA"></param>
    /// <param name="treeB"></param>
    /// <returns></returns>
    public static HashSet<string> DiffTrees(MockTree treeA, MockTree treeB)
    {
        HashSet<string> result = new();

        foreach (var (path, content) in treeA.Files)
        {
            if (!treeB.Files.TryGetValue(path, out var otherContent) || otherContent != content)
            {
                result.Add(path);
            }
        }

        foreach (var (path, content) in treeB.Files)
        {
            if (!treeA.Files.ContainsKey(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>
    /// Try to merge the trees of two commits. If there's any conflict, return null.
    /// </summary>
    /// <param name="commitA"></param>
    /// <param name="commitB"></param>
    /// <returns></returns>
    public MockTree? TryMergeTrees(MockCommitId commitA, MockCommitId commitB)
    {
        var lcas = FindLCAs(commitA, commitB);
        if (lcas.Count == 0)
        {
            throw new Exception("Refusing to merge unrelated histories");
        }

        // An arbitrary choice of LCA. Enough for this mock.
        var lca = lcas[0];

        var treeA = Commits[commitA.Id].Tree;
        var treeB = Commits[commitB.Id].Tree;
        var treeLca = Commits[lca.Id].Tree;

        var diffA = DiffTrees(treeLca, treeA);
        var diffB = DiffTrees(treeLca, treeB);

        // Check for conflicts between diffs
        if (diffA.Overlaps(diffB))
        {
            return null;
        }

        // Merge the diffs
        var mergedTree = treeLca.Files.ToBuilder();
        foreach (var path in diffA)
        {
            mergedTree[path] = treeA.Files[path];
        }
        foreach (var path in diffB)
        {
            mergedTree[path] = treeB.Files[path];
        }

        return new MockTree(mergedTree.ToImmutable());
    }

    /// <summary>
    /// Try to rebase the commit range from <c>baseCommit</c> to <c>sourceCommit</c> 
    /// onto <c>targetCommit</c>.
    /// </summary>
    /// <param name="targetCommit"></param>
    /// <param name="sourceCommit"></param>
    /// <param name="baseCommit"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public MockCommitId? TryRebaseCommits(MockCommitId targetCommit, MockCommitId sourceCommit, MockCommitId baseCommit)
    {
        // Get the list of commits to rebase
        var commitsToRebase = new List<MockCommitId>();

        void dfs(int commitId)
        {
            if (commitId == sourceCommit.Id)
            {
                return;
            }
            commitsToRebase.Add(new MockCommitId(commitId));
            foreach (var parentId in Commits[commitId].ParentIds)
            {
                dfs(parentId);
            }
        }

        dfs(baseCommit.Id);
        commitsToRebase.Reverse();

        var targetBranchDiff = DiffTrees(Commits[targetCommit.Id].Tree, Commits[baseCommit.Id].Tree);

        var workingCommit = targetCommit;
        foreach (var commit in commitsToRebase)
        {
            // Diff tree
            var commitParent = Commits[commit.Id].ParentIds[0];
            var commitTree = Commits[commit.Id].Tree;

            var diff = DiffTrees(Commits[commitParent].Tree, commitTree);

            // Apply diff
            var newTree = Commits[workingCommit.Id].Tree.Files.ToBuilder();
            foreach (var path in diff)
            {
                if (targetBranchDiff.Contains(path))
                {
                    return null;
                }
                if (commitTree.Files.ContainsKey(path))
                {
                    newTree[path] = commitTree.Files[path];
                }
                else
                {
                    newTree.Remove(path);
                }
            }

            var commitInfo = Commits[commit.Id] with
            {
                Tree = new MockTree(newTree.ToImmutable()),
                ParentIds = [workingCommit.Id]
            };
            workingCommit = CreateCommit(commitInfo);
        }

        return workingCommit;
    }
}

public record MockCommit(
    string Message,
    CommitterInfo CommitterInfo,
    List<int> ParentIds,
    MockTree Tree);

public record MockBranch(string Name);

public record MockCommitId(int Id);

/// <summary>
/// A mock git tree snapshot. The key is the file path, and the value is the file content.
/// </summary>
/// <param name="Name"></param>
/// <param name="Files"></param>
public record MockTree(ImmutableDictionary<string, string> Files);

/// <summary>
/// A simple mock implementation of <see cref="IGitOperator{TRepo, TBranch, TCommitId}"/>.
/// </summary>
public class MockGitOperator : IGitOperator<MockRepo, MockBranch, MockCommitId>
{
    public MockGitOperator()
    {
    }

    private Dictionary<string, MockRepo> repos = [];


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

    public ValueTask CreateBranchAtCommitAsync(MockRepo repo, string branchName, MockCommitId commitId)
    {
        repo.CreateBranch(branchName, commitId);
        return default;
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
}
