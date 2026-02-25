using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncReferenceAccountDetailsMessageProps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "OfficerProfiles_Audit",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LastTransactionId",
                schema: "Auth",
                table: "OfficerProfiles_Audit",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "OfficerProfiles",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LastTransactionId",
                schema: "Auth",
                table: "OfficerProfiles",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "MopProfiles_Audit",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LastTransactionId",
                schema: "Auth",
                table: "MopProfiles_Audit",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "MopProfiles",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LastTransactionId",
                schema: "Auth",
                table: "MopProfiles",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "OfficerProfiles_Audit"
            );

            migrationBuilder.DropColumn(
                name: "LastTransactionId",
                schema: "Auth",
                table: "OfficerProfiles_Audit"
            );

            migrationBuilder.DropColumn(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "OfficerProfiles"
            );

            migrationBuilder.DropColumn(
                name: "LastTransactionId",
                schema: "Auth",
                table: "OfficerProfiles"
            );

            migrationBuilder.DropColumn(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "MopProfiles_Audit"
            );

            migrationBuilder.DropColumn(
                name: "LastTransactionId",
                schema: "Auth",
                table: "MopProfiles_Audit"
            );

            migrationBuilder.DropColumn(
                name: "LastSyncTimestamp",
                schema: "Auth",
                table: "MopProfiles"
            );

            migrationBuilder.DropColumn(
                name: "LastTransactionId",
                schema: "Auth",
                table: "MopProfiles"
            );
        }
    }
}
