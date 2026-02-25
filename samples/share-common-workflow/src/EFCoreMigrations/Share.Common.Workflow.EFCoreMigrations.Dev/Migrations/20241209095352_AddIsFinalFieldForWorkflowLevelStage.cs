using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AddIsFinalFieldForWorkflowLevelStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                schema: "Workflow",
                table: "WorkflowLevelStages_Audit",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                schema: "Workflow",
                table: "WorkflowLevelStages",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFinal",
                schema: "Workflow",
                table: "WorkflowLevelStages_Audit"
            );

            migrationBuilder.DropColumn(
                name: "IsFinal",
                schema: "Workflow",
                table: "WorkflowLevelStages"
            );
        }
    }
}
