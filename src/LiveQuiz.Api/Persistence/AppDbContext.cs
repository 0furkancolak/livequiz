using LiveQuiz.Contracts.Quizzes;
using LiveQuiz.Contracts.Sessions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<SessionRecord> SessionRecords => Set<SessionRecord>();

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

        builder.Entity<SessionRecord>(e =>
        {
            e.Property(x => x.QuizTitle).HasMaxLength(QuizLimits.MaxTitleLength);
            e.HasIndex(x => new { x.OwnerId, x.FinishedAtUtc });
            e.HasMany(x => x.Players).WithOne().HasForeignKey(x => x.SessionRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.SessionRecordId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SessionPlayerResult>(e => e.Property(x => x.Nickname).HasMaxLength(SessionLimits.MaxNickname));
        builder.Entity<SessionQuestionStat>(e => e.Property(x => x.Text).HasMaxLength(QuizLimits.MaxQuestionTextLength));
    }
}
