using System.Text.RegularExpressions;

namespace Rynco.Rikki.Config;

public sealed class ConfigManager
{
    private readonly ProgramConfig _config;
    public ProgramConfig Config => _config;

    public ConfigManager(ProgramConfig config)
    {
        _config = config;
        Init();
    }

    private Dictionary<string, Repo> repoByUrl = [];
    private Dictionary<string, Repo> repoById = [];

    private static readonly Regex dotGitRegex = new(@"\.git$");

    /// <summary>
    /// Perform necessary data structure initializations.
    /// </summary>
    private void Init()
    {
        foreach (var repo in _config.Repos)
        {
            try
            {
                repoById.Add(repo.Id, repo);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"Duplicate repository ID {repo.Id} found.", e);
            }

            repoByUrl[repo.Url] = repo;

            // Also handle when the URL has a .git at the end.
            if (dotGitRegex.IsMatch(repo.Url))
            {
                var repoWithoutDotGit = dotGitRegex.Replace(repo.Url, "");
                repoByUrl[repoWithoutDotGit] = repo;
            }
        }
    }

    public Repo GetRepoById(string id)
    {
        if (repoById.TryGetValue(id, out var repo))
        {
            return repo;
        }
        throw new ArgumentException($"No repository with ID {id} found.");
    }

    public Repo GetRepoByUrl(string url)
    {
        if (repoByUrl.TryGetValue(url, out var repo))
        {
            return repo;
        }
        throw new ArgumentException($"No repository with URI {url} found.");
    }
}
