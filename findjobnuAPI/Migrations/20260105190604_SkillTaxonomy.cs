using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FindjobnuService.Migrations
{
    /// <inheritdoc />
    public partial class SkillTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanonicalSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalSkills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SkillSynonyms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Synonym = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CanonicalSkillId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillSynonyms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillSynonyms_CanonicalSkills_CanonicalSkillId",
                        column: x => x.CanonicalSkillId,
                        principalTable: "CanonicalSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalSkills_ExternalId",
                table: "CanonicalSkills",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalSkills_Name",
                table: "CanonicalSkills",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalSkills_Slug",
                table: "CanonicalSkills",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillSynonyms_CanonicalSkillId",
                table: "SkillSynonyms",
                column: "CanonicalSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillSynonyms_Synonym",
                table: "SkillSynonyms",
                column: "Synonym");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkillSynonyms");

            migrationBuilder.DropTable(
                name: "CanonicalSkills");
        }
    }
}
