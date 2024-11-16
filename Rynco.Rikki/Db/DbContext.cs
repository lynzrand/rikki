using Microsoft.EntityFrameworkCore;

namespace Rynco.Rikki.Db;

public sealed class RikkiDbContext : DbContext
{
    public DbSet<Repo> Repos { get; set; } = null!;
    public DbSet<MergeQueue> MergeQueues { get; set; } = null!;
    public DbSet<PullRequest> PullRequests { get; set; } = null!;
    public DbSet<EnqueuedPullRequest> PullRequestCiInfos { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Repo>()
            .HasKey(nameof(Repo.Id));
        modelBuilder.Entity<Repo>()
            .HasIndex(nameof(Repo.Url))
            .IsUnique();
        modelBuilder.Entity<Repo>()
            .HasIndex(nameof(Repo.DisplayName));

        modelBuilder.Entity<MergeQueue>()
            .HasKey(nameof(MergeQueue.Id));
        modelBuilder.Entity<MergeQueue>()
            .HasIndex(nameof(MergeQueue.RepoId))
            .IsUnique();
        modelBuilder.Entity<MergeQueue>()
            .HasIndex(nameof(MergeQueue.TargetBranch));
        modelBuilder.Entity<MergeQueue>()
            .HasOne<Repo>()
            .WithMany()
            .HasForeignKey(mq => mq.RepoId);

        modelBuilder.Entity<PullRequest>()
            .HasKey(nameof(PullRequest.Id));
        modelBuilder.Entity<PullRequest>()
            .HasIndex(nameof(PullRequest.RepoId));
        modelBuilder.Entity<PullRequest>()
            .HasIndex(nameof(PullRequest.MergeQueueId));
        modelBuilder.Entity<PullRequest>()
            .HasIndex(nameof(PullRequest.RepoId), nameof(PullRequest.Number));
        modelBuilder.Entity<PullRequest>()
            .HasIndex(
            nameof(PullRequest.RepoId),
            nameof(PullRequest.SourceBranch),
            nameof(PullRequest.TargetBranch));
        modelBuilder.Entity<PullRequest>()
            .HasOne<Repo>()
            .WithMany()
            .HasForeignKey(pr => pr.RepoId);
        modelBuilder.Entity<PullRequest>()
            .HasOne<MergeQueue>()
            .WithMany()
            .HasForeignKey(pr => pr.MergeQueueId);

        // CI info is entirely associated with the PR, so it doesn't need a separate key.
        modelBuilder.Entity<EnqueuedPullRequest>()
            .HasKey(nameof(EnqueuedPullRequest.PullRequestId));
        modelBuilder.Entity<PullRequest>()
            .HasOne(pr => pr.CiInfo)
            .WithOne()
            .HasForeignKey<EnqueuedPullRequest>(ci => ci.PullRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        modelBuilder.Entity<EnqueuedPullRequest>()
            .HasIndex(nameof(EnqueuedPullRequest.CiNumber));
        modelBuilder.Entity<EnqueuedPullRequest>()
            .HasIndex(nameof(EnqueuedPullRequest.SequenceNumber));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {

    }
}
