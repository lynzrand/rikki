namespace Rynco.Rikki;

/// <summary>
/// Interface to manipulate the remote git repository.
/// </summary>
public interface IGitProvider
{
    public Task PullRequestSendComment(string repository, int pullRequestId, string comment);
}
