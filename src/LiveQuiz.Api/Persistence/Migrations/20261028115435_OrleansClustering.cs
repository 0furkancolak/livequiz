using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveQuiz.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrleansClustering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orleans ADO.NET membership schema, vendored unchanged from dotnet/orleans (see ADR 0016).
            // It adds its queries to the OrleansQuery table the persistence migration created.
            using var stream = typeof(OrleansClustering).Assembly
                .GetManifestResourceStream("LiveQuiz.Api.Persistence.Orleans.orleans-clustering.sql")!;
            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS orleansmembershiptable; DROP TABLE IF EXISTS orleansmembershipversiontable;");
        }
    }
}
