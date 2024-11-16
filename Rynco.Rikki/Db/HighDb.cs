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

    public RikkiDbContext DbContext => db;

    public Task<IDbContextTransaction> BeginTransaction() => db.Database.BeginTransactionAsync();

    public Task SaveChanges() => db.SaveChangesAsync();

    public Task<Repo> GetRepoByUrl(string url) => db.Repos.FirstAsync(r => r.Url == url);

    public Task<PullRequest> GetPrByRepoAndNumber(int repoId, int number) =>
        db.PullRequests.FirstAsync(pr => pr.RepoId == repoId && pr.Number == number);

    public Task<MergeQueue> GetMergeQueueById(int id) =>
        db.MergeQueues.FirstAsync(mq => mq.Id == id);

    public async Task<PullRequest?> GetTailPrInMergeQueue(MergeQueue mq)
    {
        if (mq.TailSequenceNumber == mq.HeadSequenceNumber)
        {
            return null;
        }
        return await db.PullRequests.FirstAsync(pr =>
            pr.MergeQueueId == mq.Id
            && pr.CiInfo != null
            && pr.CiInfo.SequenceNumber == mq.TailSequenceNumber - 1);
    }

    public async Task<List<PullRequest>> GetPrsInMergeQueue(MergeQueue mq)
    {
        return await db.PullRequests.Where(pr =>
            pr.MergeQueueId == mq.Id
            && pr.CiInfo != null
            && pr.CiInfo.SequenceNumber < mq.TailSequenceNumber
            && pr.CiInfo.SequenceNumber >= mq.HeadSequenceNumber
        ).ToListAsync();
    }

    public async Task<MergeQueue> GetMergeQueueAssociatedWithCi(int ciId)
    {
        return await db.PullRequestCiInfos.Join(
            db.PullRequests,
            ci => ci.PullRequestId,
            pr => pr.Id,
            (ci, pr) => pr
        ).Join(
            db.MergeQueues,
            pr => pr.MergeQueueId,
            mq => mq.Id,
            (pr, mq) => mq
        ).Select(mq => mq).FirstAsync();
    }
}
