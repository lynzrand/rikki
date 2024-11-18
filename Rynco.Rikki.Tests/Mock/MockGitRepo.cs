using System.Collections.Immutable;
using System.Text;

namespace Rynco.Rikki.Tests;

using MockTree = ImmutableDictionary<string, string>;

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

    public MockCommit GetCommit(MockCommitId commitId)
    {
        return Commits[commitId.Id];
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

        foreach (var (path, content) in treeA)
        {
            if (!treeB.TryGetValue(path, out var otherContent) || otherContent != content)
            {
                result.Add(path);
            }
        }

        foreach (var (path, content) in treeB)
        {
            if (!treeA.ContainsKey(path))
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
        var mergedTree = treeA.ToBuilder();
        foreach (var path in diffB)
        {
            mergedTree[path] = treeB[path];
        }

        return mergedTree.ToImmutable();
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
            var newTree = Commits[workingCommit.Id].Tree.ToBuilder();
            foreach (var path in diff)
            {
                if (targetBranchDiff.Contains(path))
                {
                    return null;
                }
                if (commitTree.ContainsKey(path))
                {
                    newTree[path] = commitTree[path];
                }
                else
                {
                    newTree.Remove(path);
                }
            }

            var commitInfo = Commits[commit.Id] with
            {
                Tree = newTree.ToImmutable(),
                ParentIds = [workingCommit.Id]
            };
            workingCommit = CreateCommit(commitInfo);
        }

        return workingCommit;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Repository: {name}");
        sb.AppendLine("Commits:");
        foreach (var (id, commit) in Commits)
        {
            sb.AppendLine($"  {id}: {commit.Message}; parent: {string.Join(", ", commit.ParentIds)}");
            foreach (var branch in Branches)
            {
                if (branch.Value == id)
                {
                    sb.AppendLine("     ^-- " + branch.Key);
                }
            }
        }
        return sb.ToString();
    }
}

public record MockCommit(
    string Message,
    CommitterInfo CommitterInfo,
    List<int> ParentIds,
    MockTree Tree,
    MockCommitId? RebaseSource = null);

public record MockBranch(string Name);

public record MockCommitId(int Id)
{
    public static implicit operator MockCommitId(int id) => new(id);
    public static implicit operator int(MockCommitId id) => id.Id;
}

public class MockCheckoutedRepo
{
    MockTree tree;
    readonly MockRepo repo;
    string? headBranch = null;
    int? headCommit = null;

    public MockTree Tree => tree;
    public MockBranch? HeadBranch => headBranch == null ? null : new MockBranch(headBranch);
    public MockCommitId? Head => headCommit;

    public MockCheckoutedRepo(MockRepo repo)
    {
        this.repo = repo;
        this.headBranch = null;
        this.headCommit = null;
        this.tree = MockTree.Empty;
    }

    public MockCheckoutedRepo(MockRepo repo, string branch)
    {
        this.repo = repo;
        this.headBranch = branch;
        this.headCommit = repo.Branches[branch];
        this.tree = repo.Commits[headCommit.Value].Tree;
    }

    public void Checkout(string branch, bool create = false)
    {
        if (create)
        {
            if (repo.Branches.ContainsKey(branch))
            {
                throw new Exception("Branch already exists");
            }
            if (headCommit == null)
            {
                throw new Exception("No commit to create branch from");
            }
            repo.CreateBranch(branch, headCommit.Value);
            headBranch = branch;
        }
        else
        {
            if (!repo.Branches.ContainsKey(branch) && !create)
            {
                throw new Exception("Branch not found");
            }
            headBranch = branch;
            headCommit = repo.Branches[branch];
            tree = repo.Commits[headCommit.Value].Tree;
        }
    }

    public void SetFile(string path, string content)
    {
        tree = tree.SetItem(path, content);
    }

    public void RemoveFile(string path)
    {
        tree = tree.Remove(path);
    }

    public MockCommitId Commit(string message, CommitterInfo committerInfo)
    {
        if (headCommit == null)
        {
            // Empty repository
            headCommit = repo.CreateCommit(new MockCommit(message, committerInfo, [], tree));
            if (headBranch != null)
            {
                repo.Branches[headBranch] = headCommit.Value;
            }
            else
            {
                repo.CreateBranch("master", headCommit.Value);
            }
        }
        else
        {
            headCommit = repo.CreateCommit(new MockCommit(message, committerInfo, [headCommit.Value], tree));
            if (headBranch != null)
            {
                repo.Branches[headBranch] = headCommit.Value;
            }
        }
        return headCommit;
    }

    public bool RepoDirty()
    {
        if (headCommit == null)
        {
            return tree.IsEmpty;
        }
        else
        {
            return repo.Commits[headCommit.Value].Tree != tree;
        }
    }

    public void Pull()
    {
        if (headBranch == null)
        {
            throw new Exception("No branch checked out");
        }
        if (RepoDirty())
        {
            throw new Exception("Working tree is dirty");
        }
        var targetCommitId = repo.Branches[headBranch];
        var targetCommit = repo.Commits[targetCommitId];
        tree = targetCommit.Tree;
        headCommit = targetCommitId;
    }
}
