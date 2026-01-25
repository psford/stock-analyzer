using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedSecurities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTracked",
                schema: "data",
                table: "SecurityMaster",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TrackedSecurities",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SecurityAlias = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AddedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "system")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedSecurities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedSecurities_SecurityMaster_SecurityAlias",
                        column: x => x.SecurityAlias,
                        principalSchema: "data",
                        principalTable: "SecurityMaster",
                        principalColumn: "SecurityAlias",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityMaster_IsActive_IsTracked",
                schema: "data",
                table: "SecurityMaster",
                columns: new[] { "IsActive", "IsTracked" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityMaster_IsTracked",
                schema: "data",
                table: "SecurityMaster",
                column: "IsTracked");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedSecurities_Priority",
                schema: "data",
                table: "TrackedSecurities",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedSecurities_SecurityAlias",
                schema: "data",
                table: "TrackedSecurities",
                column: "SecurityAlias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedSecurities_Source",
                schema: "data",
                table: "TrackedSecurities",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedSecurities",
                schema: "data");

            migrationBuilder.DropIndex(
                name: "IX_SecurityMaster_IsActive_IsTracked",
                schema: "data",
                table: "SecurityMaster");

            migrationBuilder.DropIndex(
                name: "IX_SecurityMaster_IsTracked",
                schema: "data",
                table: "SecurityMaster");

            migrationBuilder.DropColumn(
                name: "IsTracked",
                schema: "data",
                table: "SecurityMaster");
        }
    }
}
