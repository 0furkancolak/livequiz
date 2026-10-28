using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveQuiz.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuizId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuizTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlayerCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionPlayerResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    CorrectAnswers = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPlayerResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionPlayerResult_SessionRecords_SessionRecordId",
                        column: x => x.SessionRecordId,
                        principalTable: "SessionRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionQuestionStat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CorrectIndex = table.Column<int>(type: "integer", nullable: false),
                    OptionCounts = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionQuestionStat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionQuestionStat_SessionRecords_SessionRecordId",
                        column: x => x.SessionRecordId,
                        principalTable: "SessionRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionPlayerResult_SessionRecordId",
                table: "SessionPlayerResult",
                column: "SessionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionQuestionStat_SessionRecordId",
                table: "SessionQuestionStat",
                column: "SessionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecords_OwnerId_FinishedAtUtc",
                table: "SessionRecords",
                columns: new[] { "OwnerId", "FinishedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionPlayerResult");

            migrationBuilder.DropTable(
                name: "SessionQuestionStat");

            migrationBuilder.DropTable(
                name: "SessionRecords");
        }
    }
}
