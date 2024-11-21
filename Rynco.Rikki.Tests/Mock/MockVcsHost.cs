using Rynco.Rikki.VcsHostService;

namespace Rynco.Rikki.Tests;

public class MockVcsHost : IVcsHostService
{
    private readonly Dictionary<(string, int), CIStatus> prCiStatus = [];
    private readonly Dictionary<(string, int), CIStatus> ciStatus = [];

    public void SetPrCiStatus(string repository, int pullRequestId, CIStatus status)
    {
        prCiStatus[(repository, pullRequestId)] = status;
    }

    public void SetCiStatus(string repository, int ciNumber, CIStatus status)
    {
        ciStatus[(repository, ciNumber)] = status;
    }

    public Task AbortCI(string repository, int ciNumber)
    {
        return Task.CompletedTask;
    }

    public string formatPrNumber(int prNumber)
    {
        return $"#{prNumber}";
    }

    public Task<CIStatus> PullRequestCheckCIStatus(string repository, int pullRequestId)
    {
        return Task.FromResult(prCiStatus[(repository, pullRequestId)]);
    }

    public Task PullRequestSendComment(string repository, int pullRequestId, string comment)
    {
        return Task.CompletedTask;
    }

    public Task<CIStatus> CheckCIStatus(string repository, int ciNumber)
    {
        return Task.FromResult(ciStatus[(repository, ciNumber)]);
    }
}
