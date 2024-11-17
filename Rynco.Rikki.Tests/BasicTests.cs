using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rynco.Rikki.Db;

namespace Rynco.Rikki.Tests;

public class BasicTests
{
    MockGitOperator mockGitOperator = null!;
    MockVcsHost mockVcsHost = null!;
    HighDb db = null!;
    HighLogic<MockRepo, MockBranch, MockCommitId> highLogic = null!;

    const string mockRepoName = "mockRepo";
    const string mockTargetBranch = "master";
    const string mockWorkingBranch = "merge-queue";
    static readonly CommitterInfo mockCommitter = new("Mock Committer", "i@example.com");

    [SetUp]
    public async Task Setup()
    {
        // Setup mock git repository
        mockGitOperator = new MockGitOperator();
        var repo = mockGitOperator.CreateRepository(mockRepoName);
        var tree = ImmutableDictionary<string, string>.Empty;
        tree = tree.Add("file1.txt", "Hello, world!");
        tree.Add("file1.txt", "Hello, world!");
        var master = repo.CreateCommit(new MockCommit("Init", mockCommitter, [], tree));
        await mockGitOperator.CreateBranchAtCommitAsync(repo, mockTargetBranch, master);
        await mockGitOperator.CreateBranchAtCommitAsync(repo, mockWorkingBranch, master);

        mockVcsHost = new MockVcsHost();

        // Setup DB
        var sqliteConn = new SqliteConnection("Filename=:memory:");
        sqliteConn.Open();
        var dbOptions = new DbContextOptionsBuilder<RikkiDbContext>()
            .UseSqlite(sqliteConn)
            .Options;

        var db = new RikkiDbContext(dbOptions);
        db.Database.EnsureCreated();

        var dbRepo = db.Add(new Repo
        {
            Url = mockRepoName,
            DisplayName = mockRepoName,
            Kind = RepoKind.Gitlab,
            MergeStyle = MergeStyle.Merge
        });
        db.SaveChanges();
        var dbMq = db.Add(new MergeQueue
        {
            RepoId = dbRepo.Entity.Id,
            HeadSequenceNumber = 0,
            TailSequenceNumber = 0,
            TargetBranch = mockTargetBranch,
            WorkingBranch = mockWorkingBranch
        });
        db.SaveChanges();
        this.db = new HighDb(db);

        highLogic = new HighLogic<MockRepo, MockBranch, MockCommitId>(this.db, mockGitOperator, mockVcsHost);
    }

    [Test]
    public async Task Test1()
    {
        var repo = await mockGitOperator.OpenAndUpdateAsync(mockRepoName);

        Console.WriteLine("Initial state:");
        Console.WriteLine(repo);

        // Add a feature branch with some commits
        var targetBranch = await mockGitOperator.GetBranchAsync(repo, mockTargetBranch);
        Assert.That(targetBranch, Is.Not.Null);
        var tip = await mockGitOperator.GetBranchTipAsync(repo, targetBranch!);
        Assert.That(tip, Is.Not.Null);
        var commit = repo.GetCommit(tip);
        var oldTree = commit.Tree;
        var newTree = oldTree.Add("file2.txt", "Hello, world!");

        const string commitMsg = "Add file2.txt";
        var newCommit = repo.CreateCommit(new MockCommit(commitMsg, mockCommitter, [tip], newTree));
        await mockGitOperator.CreateBranchAtCommitAsync(repo, "feature", newCommit);

        Console.WriteLine("Repo after feature branch added:");
        Console.WriteLine(repo);

        // Create a PR and add it to merge queue
        await highLogic.OnPrAdded(mockRepoName, 1, 0, "feature", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 1, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 1, mockCommitter);

        // Now the PR should be merged into the working branch
        Console.WriteLine("Repo after PR added to merge queue:");
        Console.WriteLine(repo);

        var workingBranch = await mockGitOperator.GetBranchAsync(repo, mockWorkingBranch);
        Assert.That(workingBranch, Is.Not.Null);
        var workingBranchTip = await mockGitOperator.GetBranchTipAsync(repo, workingBranch!);
        Assert.That(workingBranchTip, Is.Not.Null);
        var workingBranchCommit = repo.GetCommit(workingBranchTip);
        Assert.That(workingBranchCommit.ParentIds.Count, Is.EqualTo(2), "Should be a merge commit");
        Assert.That(workingBranchCommit.ParentIds, Contains.Item((int)newCommit));
        Assert.That(workingBranchCommit.ParentIds, Contains.Item((int)tip));

        // Mock CI passed message
        await highLogic.OnCiCreate(mockRepoName, 100, mockGitOperator.FormatCommitId(workingBranchTip));

        await highLogic.OnCiFinish(mockRepoName, 100, true);

        // Now the PR should be merged into the target branch
        Console.WriteLine("Repo after PR merged into target branch:");
        Console.WriteLine(repo);

        var afterTargetBranchTip = await mockGitOperator.GetBranchTipAsync(repo, targetBranch!);
        Assert.That(afterTargetBranchTip, Is.EqualTo(workingBranchTip));
    }
}
