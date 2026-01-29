using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsEodhdCompleteToSecurityMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEodhdComplete",
                schema: "data",
                table: "SecurityMaster",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEodhdComplete",
                schema: "data",
                table: "SecurityMaster");
        }
    }
}
