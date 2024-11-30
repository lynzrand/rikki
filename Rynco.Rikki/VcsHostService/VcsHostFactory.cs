using Rynco.Rikki.Config;

namespace Rynco.Rikki.VcsHostService;

/// <summary>
/// Factory for creating VCS client from repository configuration.
/// This is the interface implementations should rely on.
/// </summary>
public interface IVcsHostFactory
{
    /// <summary>
    /// Create a VCS client from the repository configuration.
    /// </summary>
    /// <param name="repository"></param>
    /// <returns></returns>
    public IVcsHostService GetVcsHostFor(Repo repo);
}

public sealed class VcsHostFactory : IVcsHostFactory
{
    public IVcsHostService GetVcsHostFor(Repo repo)
    {
        switch (repo.Kind)
        {
            case RepoKind.Gitlab:
                {
                    var repoUrl = new Uri(repo.Url);
                    var baseUri = repoUrl.GetLeftPart(UriPartial.Authority);
                    return new GitLabService(baseUri, repo.Token);
                }
            default:
                throw new ArgumentException($"Unsupported repository kind {repo.Kind}.");
        }
    }
}
