namespace Rynco.Rikki.VcsHostService;

public class GiteaService : IVcsHostService
{
    public Task AbortCI(string repository, int ciNumber)
    {
        throw new NotImplementedException();
    }

    public Task<CIStatus> CheckCIStatus(string repository, int ciNumber)
    {
        throw new NotImplementedException();
    }

    public string formatPrNumber(int prNumber)
    {
        return $"#{prNumber}";
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
