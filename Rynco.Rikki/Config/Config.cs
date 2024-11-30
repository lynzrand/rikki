namespace Rynco.Rikki.Config;

public record class ProgramConfig
{

    /// <summary>
    /// Repositories that Rikki is managing.
    /// </summary>
    public required List<Repo> Repos { get; init; }
}

/// <summary>
/// The kind of repository. Only Gitlab is supported at the moment.
/// </summary>
public enum RepoKind
{
    Gitlab = 0
}

/// <summary>
/// The merge fashion for a repository.
/// </summary>
public enum MergeStyle
{
    /// <summary>
    /// Merge using merge commits.
    /// </summary>
    Merge = 0,

    /// <summary>
    /// Merge using rebase.
    /// </summary>
    Linear = 1,

    /// <summary>
    /// First rebase, and then create a merge commit.
    /// </summary>
    SemiLinear = 2
}

public record class Repo
{
    /// <summary>
    /// The ID to be used to identify the repository. Should be unique among all repositories,
    /// and should not change once set.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The URL of the repository.
    /// </summary>
    public required string Url;

    /// <summary>
    /// The display name of the repository.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The kind of repository.
    /// </summary>
    public required RepoKind Kind { get; set; }

    /// <summary>
    /// Access token for the repository.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The merge style for the repository.
    /// </summary>
    public required MergeStyle MergeStyle { get; set; }
}
