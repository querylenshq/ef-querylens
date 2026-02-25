using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class StageIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StageIdentifier",
                schema: "Workflow",
                table: "WorkflowLevelStages_Audit",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "StageIdentifier",
                schema: "Workflow",
                table: "WorkflowLevelStages",
                type: "int",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StageIdentifier",
                schema: "Workflow",
                table: "WorkflowLevelStages_Audit"
            );

            migrationBuilder.DropColumn(
                name: "StageIdentifier",
                schema: "Workflow",
                table: "WorkflowLevelStages"
            );
        }
    }
}
