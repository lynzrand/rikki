namespace Rynco.Rikki.VcsHostService;

/// <summary>
/// Interface to manipulate the remote git repository.
/// </summary>
public interface IVcsHostService
{
    /// <summary>
    /// Format a PR number to a string. Should be the idiomatically correct way to format a PR 
    /// number, like <c>#123</c> for GitHub or <c>!123</c> for GitLab.
    /// </summary>
    /// <param name="prNumber"></param>
    /// <returns></returns>
    public string formatPrNumber(int prNumber);

    /// <summary>
    /// Send a comment to a given pull request.
    /// </summary>
    /// <param name="repository"></param>
    /// <param name="pullRequestId"></param>
    /// <param name="comment"></param>
    /// <returns></returns>
    public Task PullRequestSendComment(string repository, int pullRequestId, string comment);

    /// <summary>
    /// Check the status of the CI run for the given pull request.
    /// </summary>
    /// <param name="repository"></param>
    /// <param name="pullRequestId"></param>
    /// <returns></returns>
    public Task<CIStatus> PullRequestCheckCIStatus(string repository, int pullRequestId);

    /// <summary>
    /// Abort the given CI run.
    /// </summary>
    /// <param name="repository"></param>
    /// <param name="ciNumber"></param>
    /// <returns></returns>
    public Task AbortCI(string repository, int ciNumber);

    /// <summary>
    /// Check the status of the given CI run.
    /// </summary>
    /// <param name="repository"></param>
    /// <param name="ciNumber"></param>
    /// <returns></returns>
    public Task<CIStatus> CheckCIStatus(string repository, int ciNumber);
}

public enum CIStatus
{
    Passed, Failed, NotFinished
}
