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


    /// <summary>
    /// The simplest merge scenario: a PR is created and merged into the target branch.
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task PlainMerge()
    {
        var repo = await mockGitOperator.OpenAndUpdateAsync(mockRepoName);
        var workingRepo = new MockCheckoutedRepo(repo, "master");

        // Add a feature branch with some commits
        workingRepo.Checkout("feature", true);
        workingRepo.SetFile("file2.txt", "Hello, world!");
        var featureCommit = workingRepo.Commit("Add file2.txt", mockCommitter);

        var targetBranch = repo.GetBranch(mockTargetBranch)!;
        var originalMasterTip = await mockGitOperator.GetBranchTipAsync(repo, targetBranch);

        // Create a PR and add it to merge queue
        await highLogic.OnPrAdded(mockRepoName, 1, 0, "feature", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 1, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 1, mockCommitter);

        // Now the PR should be merged into the working branch
        var workingBranch = await mockGitOperator.GetBranchAsync(repo, mockWorkingBranch);
        var workingBranchTip = await mockGitOperator.GetBranchTipAsync(repo, workingBranch!);
        var workingBranchCommit = repo.GetCommit(workingBranchTip);

        Assert.That(workingBranchCommit.ParentIds,
            Is.EqualTo(new List<int> { originalMasterTip.Id, featureCommit.Id }));

        // Mock CI passed message
        await highLogic.OnCiCreate(mockRepoName, 100, mockGitOperator.FormatCommitId(workingBranchTip));

        await highLogic.OnCiFinish(mockRepoName, 100, true);

        // Now the PR should be merged into the target branch
        var afterTargetBranchTip = await mockGitOperator.GetBranchTipAsync(repo, targetBranch);

        Assert.That(afterTargetBranchTip, Is.EqualTo(workingBranchTip));
    }

    /// <summary>
    /// Merge conflict scenario: two PRs are created and added to the merge queue, but the second PR
    /// cannot be merged because of a conflict.
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task PlainMergeConflict()
    {
        var repo = await mockGitOperator.OpenAndUpdateAsync(mockRepoName);
        var workingRepo = new MockCheckoutedRepo(repo, "master");

        // Add two feature branches with conflicting commits
        workingRepo.Checkout("feature1", true);
        workingRepo.SetFile("file1.txt", "No I'm not going to say hello!");
        var featureCommit = workingRepo.Commit("Change file1.txt", mockCommitter);

        workingRepo.Checkout("master");
        workingRepo.Checkout("feature2", true);
        workingRepo.SetFile("file1.txt", "Hello world it's my go!!!!!");
        var featureCommit2 = workingRepo.Commit("Change file1.txt in feature branch 2", mockCommitter);

        var targetBranch = repo.GetBranch(mockTargetBranch)!;

        // Add branch1 to merge queue
        await highLogic.OnPrAdded(mockRepoName, 1, 0, "feature1", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 1, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 1, mockCommitter);

        // Add branch2 to merge queue, should fail
        await highLogic.OnPrAdded(mockRepoName, 2, 0, "feature2", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 2, VcsHostService.CIStatus.Passed);
        Assert.ThrowsAsync(typeof(FailedToMergeException),
            () => highLogic.OnRequestToAddToMergeQueue(mockRepoName, 2, mockCommitter)
        );
    }

    [Test]
    public async Task OneFailure()
    {
        var repo = await mockGitOperator.OpenAndUpdateAsync(mockRepoName);
        var workingRepo = new MockCheckoutedRepo(repo, "master");

        // Add a feature branch with some commits
        workingRepo.Checkout("feature", true);
        workingRepo.SetFile("file2.txt", "Hello, world!");
        var featureCommit = workingRepo.Commit("Add file2.txt", mockCommitter);

        var targetBranch = repo.GetBranch(mockTargetBranch)!;

        // Create a PR and add it to merge queue
        await highLogic.OnPrAdded(mockRepoName, 1, 0, "feature", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 1, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 1, mockCommitter);

        var workingBranch = await mockGitOperator.GetBranchAsync(repo, mockWorkingBranch);
        var workingBranchTip = await mockGitOperator.GetBranchTipAsync(repo, workingBranch!);

        // But the subsequent CI failed
        await highLogic.OnCiCreate(mockRepoName, 100, mockGitOperator.FormatCommitId(workingBranchTip));
        await highLogic.OnCiFinish(mockRepoName, 100, false);

        // The PR should be removed from the merge queue
        var dbRepo = await db.GetRepoByUrl(mockRepoName);
        var pr = await db.GetPrByRepoAndNumber(dbRepo.Id, 1);
        Assert.That(pr.CiInfo, Is.Null);
    }

    [Test]
    public async Task OneFailureWhenTwoPrs()
    {
        var repo = await mockGitOperator.OpenAndUpdateAsync(mockRepoName);
        var workingRepo = new MockCheckoutedRepo(repo, "master");

        // Add a feature branch with some commits
        workingRepo.Checkout("feature", true);
        workingRepo.SetFile("file2.txt", "Hello, world!");
        var featureCommit = workingRepo.Commit("Add file2.txt", mockCommitter);

        var targetBranch = repo.GetBranch(mockTargetBranch)!;

        // Create a PR and add it to merge queue
        await highLogic.OnPrAdded(mockRepoName, 1, 0, "feature", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 1, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 1, mockCommitter);

        var workingBranch = await mockGitOperator.GetBranchAsync(repo, mockWorkingBranch);
        var workingBranchTip1 = await mockGitOperator.GetBranchTipAsync(repo, workingBranch!);

        // Add another PR
        workingRepo.Checkout("feature2", true);
        workingRepo.SetFile("file3.txt", "Hello, world!");
        var featureCommit2 = workingRepo.Commit("Add file3.txt", mockCommitter);

        await highLogic.OnPrAdded(mockRepoName, 2, 0, "feature2", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 2, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 2, mockCommitter);

        // But the subsequent CI failed
        await highLogic.OnCiCreate(mockRepoName, 100, mockGitOperator.FormatCommitId(workingBranchTip1));
        await highLogic.OnCiFinish(mockRepoName, 100, false);

        // The PR should be removed from the merge queue
        var dbRepo = await db.GetRepoByUrl(mockRepoName);
        var pr = await db.GetPrByRepoAndNumber(dbRepo.Id, 1);
        Assert.That(pr.CiInfo, Is.Null);
    }

    [Test]
    public async Task LatterPrFinishesEarlierThanFormer()
    {
        var repo = await mockGitOperator.OpenAndUpdateAsync(mockRepoName);
        var workingRepo = new MockCheckoutedRepo(repo, "master");

        // Add a feature branch with some commits
        workingRepo.Checkout("feature", true);
        workingRepo.SetFile("file2.txt", "Hello, world!");
        var featureCommit = workingRepo.Commit("Add file2.txt", mockCommitter);

        var targetBranch = repo.GetBranch(mockTargetBranch)!;
        var originalMasterCommit = mockGitOperator.GetBranchTipAsync(repo, targetBranch).Result;

        // Create a PR and add it to merge queue
        await highLogic.OnPrAdded(mockRepoName, 1, 0, "feature", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 1, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 1, mockCommitter);

        var workingBranch = await mockGitOperator.GetBranchAsync(repo, mockWorkingBranch);
        // This is the merge commit of the first PR
        var workingBranchTip1 = await mockGitOperator.GetBranchTipAsync(repo, workingBranch!);

        // Add another PR
        workingRepo.Checkout("feature2", true);
        workingRepo.SetFile("file3.txt", "Hello, world!");
        var featureCommit2 = workingRepo.Commit("Add file3.txt", mockCommitter);

        await highLogic.OnPrAdded(mockRepoName, 2, 0, "feature2", "master");
        mockVcsHost.SetCiStatus(mockRepoName, 2, VcsHostService.CIStatus.Passed);
        await highLogic.OnRequestToAddToMergeQueue(mockRepoName, 2, mockCommitter);

        // This is the merge commit of the second PR
        var workingBranchTip2 = await mockGitOperator.GetBranchTipAsync(repo, workingBranch!);

        // Now we create CI for both PRs
        await highLogic.OnCiCreate(mockRepoName, 100, mockGitOperator.FormatCommitId(workingBranchTip1));
        await highLogic.OnCiCreate(mockRepoName, 101, mockGitOperator.FormatCommitId(workingBranchTip2));

        // And let the second PR finish first
        await highLogic.OnCiFinish(mockRepoName, 101, true);

        // Nothing should happen in the merge queue
        var masterCommit1 = await mockGitOperator.GetBranchTipAsync(repo, targetBranch);
        Assert.That(masterCommit1, Is.EqualTo(originalMasterCommit));

        // Now let the first PR finish
        await highLogic.OnCiFinish(mockRepoName, 100, true);

        // The merge queue should be updated directly to the second PR
        var masterCommit2 = await mockGitOperator.GetBranchTipAsync(repo, targetBranch);
        Assert.That(masterCommit2, Is.EqualTo(workingBranchTip2));
    }
}
