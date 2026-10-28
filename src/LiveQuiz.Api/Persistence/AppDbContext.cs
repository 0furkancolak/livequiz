using Microsoft.EntityFrameworkCore;

namespace LiveQuiz.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
