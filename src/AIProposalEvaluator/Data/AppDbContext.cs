using AIProposalEvaluator.Models;
using Microsoft.EntityFrameworkCore;

namespace AIProposalEvaluator.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProposalEvaluation> ProposalEvaluations => Set<ProposalEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProposalEvaluation>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
