using LiveQuiz.Contracts.Quizzes;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Quiz>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(QuizLimits.MaxTitleLength);
            e.Property(x => x.Description).HasMaxLength(QuizLimits.MaxDescriptionLength);
            e.HasIndex(x => x.OwnerId);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Question>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(QuizLimits.MaxQuestionTextLength);
            e.HasIndex(x => new { x.QuizId, x.Order }).IsUnique();
            e.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AnswerOption>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(QuizLimits.MaxOptionTextLength);
            e.HasIndex(x => new { x.QuestionId, x.Order }).IsUnique();
        });
    }
}
