using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FxRates.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Rates",
                columns: table => new
                {
                    BaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Bid = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Ask = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProviderQuotedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rates", x => new { x.BaseCurrency, x.QuoteCurrency });
                    table.CheckConstraint("CK_Rates_DifferentCurrencies", "\"BaseCurrency\" <> \"QuoteCurrency\"");
                    table.CheckConstraint("CK_Rates_Prices", "\"Bid\" > 0 AND \"Ask\" >= \"Bid\"");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rates");
        }
    }
}
