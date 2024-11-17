using Rynco.Rikki.VcsHostService;

namespace Rynco.Rikki.GitOperator;

public class GitLabService : IVcsHostService
{
    public Task AbortCI(string repository, int ciNumber)
    {
        throw new NotImplementedException();
    }

    public string formatPrNumber(int prNumber)
    {
        throw new NotImplementedException();
    }

    public Task<CIStatus> PullRequestCheckCIStatus(string repository, int pullRequestId)
    {
        throw new NotImplementedException();
    }

    public Task PullRequestSendComment(string repository, int pullRequestId, string comment)
    {
        throw new NotImplementedException();
    }
}
