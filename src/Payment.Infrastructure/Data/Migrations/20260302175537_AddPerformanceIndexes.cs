using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payment.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReplayed",
                table: "DeadLetterMessages");

            migrationBuilder.AddColumn<string>(
                name: "ReplayError",
                table: "DeadLetterMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplayedOutboxMessageId",
                table: "DeadLetterMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "DeadLetterMessages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPaymentStates_LicenseId_Month",
                table: "MonthlyPaymentStates",
                columns: new[] { "LicenseId", "Month" },
                unique: true,
                filter: "[CurrentState] <> 'Completed' AND [CurrentState] <> 'Overdue'");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterMessages_ReplayedOutboxMessageId",
                table: "DeadLetterMessages",
                column: "ReplayedOutboxMessageId");

            // Filtered index so the OutboxProcessor polling query uses an index-seek
            // rather than a full scan as the table grows to millions of processed rows.
            // IX_IdempotencyKeys_CreatedAt already exists (added in ProductionHardening migration).
            migrationBuilder.Sql(@"
                CREATE INDEX IX_OutboxMessages_Unprocessed
                ON OutboxMessages (CreatedAt ASC)
                WHERE ProcessedAt IS NULL AND RetryCount < 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonthlyPaymentStates_LicenseId_Month",
                table: "MonthlyPaymentStates");

            migrationBuilder.DropIndex(
                name: "IX_DeadLetterMessages_ReplayedOutboxMessageId",
                table: "DeadLetterMessages");

            migrationBuilder.DropColumn(
                name: "ReplayError",
                table: "DeadLetterMessages");

            migrationBuilder.DropColumn(
                name: "ReplayedOutboxMessageId",
                table: "DeadLetterMessages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DeadLetterMessages");

            migrationBuilder.AddColumn<bool>(
                name: "IsReplayed",
                table: "DeadLetterMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_OutboxMessages_Unprocessed ON OutboxMessages");
        }
    }
}
