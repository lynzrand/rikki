using Rynco.Rikki.Config;
using Rynco.Rikki.Db;
using Rynco.Rikki.VcsHostService;

namespace Rynco.Rikki;

/// <summary>
/// The interface of <see cref="HighLogic"/>. This interface is created solely for hiding
/// the type parameters, which are not exposed in the public API.
/// </summary>
public interface IHighLogic
{
    public Task OnPrAdded(string uri, int prNumber, int priority, string sourceBranch, string targetBranch);
    public Task OnRequestToAddToMergeQueue(string uri, int prNumber, CommitterInfo info);
    public Task OnCiCreate(string repo, int ciNumber, string associatedCommit);
    public Task OnCiFinish(string repo, int ciNumber, bool success);
}

/// <summary>
/// Creates HighLogic instances. Used as a class instead of a callback for being able to use
/// dependency injection.
/// </summary>
/// <typeparam name="TRepo"></typeparam>
/// <typeparam name="TBranch"></typeparam>
/// <typeparam name="TCommitId"></typeparam>
public sealed class HighLogicFactory<TRepo, TBranch, TCommitId> : IHighLogicFactory
    where TCommitId : IEquatable<TCommitId>
{
    public HighLogicFactory(
        ConfigManager config,
        HighDb db,
        IGitOperator<TRepo, TBranch, TCommitId> gitOperator)
    {
        _config = config;
        _db = db;
        _gitOperator = gitOperator;
    }

    private readonly ConfigManager _config;
    private readonly HighDb _db;
    private readonly IGitOperator<TRepo, TBranch, TCommitId> _gitOperator;

    public IHighLogic Create(IVcsHostService vcsHostService)
    {
        return new HighLogic<TRepo, TBranch, TCommitId>(_config, _db, _gitOperator, vcsHostService);
    }
}

/// <summary>
/// Another interface type to hide the type parameters of <see cref="HighLogicFactory{TRepo,TBranch,TCommitId}"/>.
/// </summary>
public interface IHighLogicFactory
{
    public IHighLogic Create(IVcsHostService vcsHostService);
}
