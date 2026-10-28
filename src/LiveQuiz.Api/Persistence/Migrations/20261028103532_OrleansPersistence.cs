using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveQuiz.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrleansPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orleans ADO.NET persistence schema, vendored unchanged from dotnet/orleans (see ADR 0010).
            using var stream = typeof(OrleansPersistence).Assembly
                .GetManifestResourceStream("LiveQuiz.Api.Persistence.Orleans.orleans-persistence.sql")!;
            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS writetostorage; DROP TABLE IF EXISTS orleansstorage; DROP TABLE IF EXISTS orleansquery;");
        }
    }
}
