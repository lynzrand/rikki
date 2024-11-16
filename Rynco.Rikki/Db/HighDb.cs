using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rynco.Rikki.Db;

/// <summary>
/// High-level DB operations. This is a wrapper around the DbContext, so it should
/// obey the same lifetime rules.
/// </summary>
public class HighDb(RikkiDbContext db)
{
    private readonly RikkiDbContext db = db;

    public Task<IDbContextTransaction> BeginTransaction() => db.Database.BeginTransactionAsync();

    public Task<Repo> GetRepoByUrl(string url) => db.Repos.FirstAsync(r => r.Url == url);

    public Task<PullRequest> GetPrByRepoAndNumber(int repoId, int number) =>
        db.PullRequests.FirstAsync(pr => pr.RepoId == repoId && pr.Number == number);

    public Task<MergeQueue> GetMergeQueueById(int id) =>
        db.MergeQueues.FirstAsync(mq => mq.Id == id);
}
