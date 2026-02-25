using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class ManualCreatedAtColumnForStageActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApplicationCreatedAtValue",
                schema: "Application",
                table: "AppWorkflowLevelStageActions_Audit",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ApplicationCreatedAtValue",
                schema: "Application",
                table: "AppWorkflowLevelStageActions",
                type: "datetime(6)",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationCreatedAtValue",
                schema: "Application",
                table: "AppWorkflowLevelStageActions_Audit"
            );

            migrationBuilder.DropColumn(
                name: "ApplicationCreatedAtValue",
                schema: "Application",
                table: "AppWorkflowLevelStageActions"
            );
        }
    }
}
