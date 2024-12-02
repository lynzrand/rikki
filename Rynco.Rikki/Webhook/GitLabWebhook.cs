using Microsoft.AspNetCore.Mvc;
using NGitLab;
using Rynco.Rikki.Config;
using Rynco.Rikki.VcsHostService;
using Serde;

namespace Rynco.Rikki.Webhook;

[ApiController]
[Route("/api/v1/webhook/gitlab")]
public sealed class GitLabWebhook : ControllerBase
{
    private readonly IHighLogicFactory factory;
    private readonly ConfigManager config;

    public GitLabWebhook(IHighLogicFactory factory, ConfigManager config)
    {
        this.factory = factory;
        this.config = config;
    }

    [Route("/pr")]
    [HttpPost]
    public async Task OnPullRequestEvent([FromBody] MergeRequestEvent evt)
    {
        string gitHttpUrl = evt.Project.GitHttpUrl;
        Repo repo;
        try { repo = config.GetRepoByUrl(gitHttpUrl); }
        catch
        {
            // We don't have the repo in our config. We can't do anything.
            return;
        }

        if (evt.ObjectAttributes.Action is not MergeRequestAction.Open)
            return; // not interested in this event

        var serverUrl = new Uri(gitHttpUrl).GetLeftPart(UriPartial.Authority);
        var gitlabService = new GitLabService(serverUrl, repo.Token);
        await factory.Create(gitlabService).OnPrAdded(
            gitHttpUrl,
            evt.ObjectAttributes.Iid,
            0, // priority
            evt.ObjectAttributes.SourceBranch,
            evt.ObjectAttributes.TargetBranch
        );
    }

    [Route("/ci")]
    [HttpPost]
    public async Task OnCIEvent([FromBody] PipelineEvent evt)
    {
        string gitHttpUrl = evt.Project.GitHttpUrl;
        Repo repo;
        try { repo = config.GetRepoByUrl(gitHttpUrl); }
        catch
        {
            // We don't have the repo in our config. We can't do anything.
            return;
        }

        var serverUrl = new Uri(gitHttpUrl).GetLeftPart(UriPartial.Authority);
        var gitlabService = new GitLabService(serverUrl, repo.Token);

        if (!IsPipelineEnding(evt.ObjectAttributes.Status) && evt.ObjectAttributes.Status != JobStatus.Created)
            return; // not interested in this event

        var svc = factory.Create(gitlabService);
        if (evt.ObjectAttributes.Status == JobStatus.Created)
        {
            await svc.OnCiCreate(gitHttpUrl, evt.ObjectAttributes.Iid, evt.ObjectAttributes.Sha);
        }
        else
        {
            await svc.OnCiFinish(gitHttpUrl, evt.ObjectAttributes.Iid, evt.ObjectAttributes.Status == JobStatus.Success);
        }
    }

    private static bool IsPipelineEnding(JobStatus status)
    {
        return status switch
        {
            JobStatus.Failed |
            JobStatus.Canceled |
            JobStatus.Success |
            JobStatus.Skipped => true,
            _ => false
        };
    }

    // We don't need all the fields for the models. These are just fields we are interested in.

    public enum MergeRequestAction
    {
        Open,
        Close,
        Reopen,
        Update,
        Approved,
        Unapproved,
        Approval,
        Unapproval,
        Merge
    }

    public record ProjectInfo(
        int Id,
        string Name,
        string Description,
        string GitHttpUrl
    );

    public record MergeRequestObjectAttr(
        int Iid,
        string TargetBranch,
        string SourceBranch,
        MergeRequestAction Action
    );

    public record MergeRequestEvent(ProjectInfo Project, MergeRequestObjectAttr ObjectAttributes);

    public record PipelineObjectAttr(
        int Iid,
        JobStatus Status,
        string Sha
    );

    public record PipelineEvent(ProjectInfo Project, PipelineObjectAttr ObjectAttributes);
}

