namespace Rynco.Rikki.VcsHostService;

/// <summary>
/// Interface to manipulate the remote git repository.
/// </summary>
public interface IVcsHostService
{
    public Task PullRequestSendComment(string repository, int pullRequestId, string comment);
    public Task AbortCI(string repository, int ciNumber);
}
