using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payment.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductionHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "OutboxMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "MonthlyPaymentStates",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "TimeoutAt",
                table: "MonthlyPaymentStates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeadLetterMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalOutboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    OriginalCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeadLetteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsReplayed = table.Column<bool>(type: "bit", nullable: false),
                    ReplayedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPaymentStates_TimeoutAt",
                table: "MonthlyPaymentStates",
                column: "TimeoutAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterMessages_DeadLetteredAt",
                table: "DeadLetterMessages",
                column: "DeadLetteredAt");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_CreatedAt",
                table: "IdempotencyKeys",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeadLetterMessages");

            migrationBuilder.DropTable(
                name: "IdempotencyKeys");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyPaymentStates_TimeoutAt",
                table: "MonthlyPaymentStates");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "MonthlyPaymentStates");

            migrationBuilder.DropColumn(
                name: "TimeoutAt",
                table: "MonthlyPaymentStates");
        }
    }
}
