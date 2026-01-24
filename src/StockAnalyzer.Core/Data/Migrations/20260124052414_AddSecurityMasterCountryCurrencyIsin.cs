using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityMasterCountryCurrencyIsin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "data",
                table: "SecurityMaster",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "data",
                table: "SecurityMaster",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Isin",
                schema: "data",
                table: "SecurityMaster",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                schema: "data",
                table: "SecurityMaster");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "data",
                table: "SecurityMaster");

            migrationBuilder.DropColumn(
                name: "Isin",
                schema: "data",
                table: "SecurityMaster");
        }
    }
}
