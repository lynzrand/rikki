using Rynco.Rikki.VcsHostService;

namespace Rynco.Rikki.Tests;

public class MockVcsHost : IVcsHostService
{
    private Dictionary<(string, int), CIStatus> ciStatuses = new();

    public void SetCiStatus(string repository, int pullRequestId, CIStatus status)
    {
        ciStatuses[(repository, pullRequestId)] = status;
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
        return Task.FromResult(ciStatuses[(repository, pullRequestId)]);
    }

    public Task PullRequestSendComment(string repository, int pullRequestId, string comment)
    {
        return Task.CompletedTask;
    }
}
