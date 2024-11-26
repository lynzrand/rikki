using Rynco.Rikki.VcsHostService;
using NGitLab;
using NGitLab.Models;
using System.Text.RegularExpressions;

namespace Rynco.Rikki.GitOperator;

public class GitLabService(string serverUrl, string apiToken) : IVcsHostService
{
    readonly GitLabClient client = new(serverUrl, apiToken);

    public async Task AbortCI(string repository, int ciNumber)
    {
        var pipelineClient = client.GetPipelines(new ProjectId(repository));
        var pipelineJobs = pipelineClient.GetJobsAsync(new PipelineJobQuery
        {
            PipelineId = ciNumber
        });
        var jobClient = client.GetJobs(new ProjectId(repository));
        await Task.WhenAll(pipelineJobs.Select(job => jobClient.RunActionAsync(job.Id, JobAction.Cancel)));
    }

    public async Task<CIStatus> CheckCIStatus(string repository, int ciNumber)
    {
        var pipelineClient = client.GetPipelines(new ProjectId(repository));
        var pipeline = await pipelineClient.GetByIdAsync(ciNumber);
        return PipelineStatusToCiStatus(pipeline);

    }

    public string formatPrNumber(int prNumber)
    {
        return $"!{prNumber}";
    }

    public async Task<CIStatus> PullRequestCheckCIStatus(string repository, int pullRequestId)
    {
        var prs = client.GetMergeRequest(new ProjectId(repository));
        var pr = await prs.GetByIidAsync(pullRequestId, new SingleMergeRequestQuery());
        var pipeline = pr.HeadPipeline;
        return PipelineStatusToCiStatus(pipeline);
    }

    private static CIStatus PipelineStatusToCiStatus(Pipeline pipeline)
    {
        return pipeline.Status switch
        {
            JobStatus.Success => CIStatus.Passed,
            JobStatus.Failed => CIStatus.Failed,
            JobStatus.Running => CIStatus.NotFinished,
            JobStatus.Unknown => CIStatus.NotFinished,
            JobStatus.Pending => CIStatus.NotFinished,
            JobStatus.Created => CIStatus.NotFinished,
            JobStatus.Canceled => CIStatus.Failed,
            JobStatus.Skipped => CIStatus.Passed,
            JobStatus.Manual => CIStatus.NotFinished,
            JobStatus.NoBuild => CIStatus.NotFinished,
            JobStatus.Preparing => CIStatus.NotFinished,
            JobStatus.WaitingForResource => CIStatus.NotFinished,
            JobStatus.Scheduled => CIStatus.NotFinished,
            JobStatus.Canceling => CIStatus.Failed,
            _ => CIStatus.NotFinished
        };
    }

    public async Task PullRequestSendComment(string repository, int pullRequestId, string comment)
    {
        await Task.Run(() =>
        {
            var prs = client.GetMergeRequest(new ProjectId(repository));
            prs.Comments(pullRequestId).Add(new MergeRequestCommentCreate()
            {
                Body = comment
            });
        });
    }
}
