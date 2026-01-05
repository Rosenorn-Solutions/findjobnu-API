using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FindjobnuService.Migrations
{
    /// <inheritdoc />
    public partial class AddMLRecommendationSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserJobInteractions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    InteractionType = table.Column<int>(type: "int", nullable: false),
                    InteractionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserJobInteractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserJobInteractions_JobIndexPostingsExtended_JobId",
                        column: x => x.JobId,
                        principalTable: "JobIndexPostingsExtended",
                        principalColumn: "JobID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserJobInteractions_InteractionDate",
                table: "UserJobInteractions",
                column: "InteractionDate");

            migrationBuilder.CreateIndex(
                name: "IX_UserJobInteractions_JobId",
                table: "UserJobInteractions",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_UserJobInteractions_UserId",
                table: "UserJobInteractions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserJobInteractions_UserId_JobId",
                table: "UserJobInteractions",
                columns: new[] { "UserId", "JobId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserJobInteractions");
        }
    }
}
