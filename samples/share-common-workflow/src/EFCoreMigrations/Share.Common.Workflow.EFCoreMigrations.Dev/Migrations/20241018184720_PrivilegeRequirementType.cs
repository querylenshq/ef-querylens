using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable S4581

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class PrivilegeRequirementType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppWorkflowLevelStages_ApplicationOfficers_ApplicationOffice~",
                schema: "Application",
                table: "AppWorkflowLevelStages"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowLevelStagePrivileges_WorkflowLevelStages_WorkflowLev~",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges"
            );

            migrationBuilder.DropIndex(
                name: "IX_AppWorkflowLevelStages_ApplicationOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStages"
            );

            migrationBuilder.DropColumn(
                name: "IsRequiredToFinalizeLevel",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit"
            );

            migrationBuilder.DropColumn(
                name: "IsRequiredToFinalizeLevel",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges"
            );

            migrationBuilder.DropColumn(
                name: "ApplicationOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStages"
            );

            migrationBuilder.DropColumn(
                name: "WorkflowRole",
                schema: "Application",
                table: "ApplicationOfficers_Audit"
            );

            migrationBuilder.DropColumn(
                name: "WorkflowRole",
                schema: "Application",
                table: "ApplicationOfficers"
            );

            migrationBuilder.RenameColumn(
                name: "WorkflowLevelId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit",
                newName: "StageId"
            );

            migrationBuilder.RenameColumn(
                name: "WorkflowLevelId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                newName: "StageId"
            );

            migrationBuilder.RenameIndex(
                name: "IX_WorkflowLevelStagePrivileges_WorkflowLevelId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                newName: "IX_WorkflowLevelStagePrivileges_StageId"
            );

            migrationBuilder.AddColumn<int>(
                name: "PrivilegeRequirementType",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "PrivilegeRequirementType",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "ParentStageId",
                    schema: "Application",
                    table: "AppWorkflows_Audit",
                    type: "char(36)",
                    nullable: true,
                    collation: "ascii_general_ci",
                    oldClrType: typeof(Guid),
                    oldType: "char(36)"
                )
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "ParentStageId",
                    schema: "Application",
                    table: "AppWorkflows",
                    type: "char(36)",
                    nullable: true,
                    collation: "ascii_general_ci",
                    oldClrType: typeof(Guid),
                    oldType: "char(36)"
                )
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers_Audit",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationOfficers_AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers",
                column: "AppWorkflowId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationOfficers_AppWorkflows_AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers",
                column: "AppWorkflowId",
                principalSchema: "Application",
                principalTable: "AppWorkflows",
                principalColumn: "AppWorkflowId",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowLevelStagePrivileges_WorkflowLevelStages_StageId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                column: "StageId",
                principalSchema: "Workflow",
                principalTable: "WorkflowLevelStages",
                principalColumn: "WorkflowLevelStageId",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationOfficers_AppWorkflows_AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowLevelStagePrivileges_WorkflowLevelStages_StageId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges"
            );

            migrationBuilder.DropIndex(
                name: "IX_ApplicationOfficers_AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers"
            );

            migrationBuilder.DropColumn(
                name: "PrivilegeRequirementType",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit"
            );

            migrationBuilder.DropColumn(
                name: "PrivilegeRequirementType",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges"
            );

            migrationBuilder.DropColumn(
                name: "AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers_Audit"
            );

            migrationBuilder.DropColumn(
                name: "AppWorkflowId",
                schema: "Application",
                table: "ApplicationOfficers"
            );

            migrationBuilder.RenameColumn(
                name: "StageId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit",
                newName: "WorkflowLevelId"
            );

            migrationBuilder.RenameColumn(
                name: "StageId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                newName: "WorkflowLevelId"
            );

            migrationBuilder.RenameIndex(
                name: "IX_WorkflowLevelStagePrivileges_StageId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                newName: "IX_WorkflowLevelStagePrivileges_WorkflowLevelId"
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredToFinalizeLevel",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredToFinalizeLevel",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "ParentStageId",
                    schema: "Application",
                    table: "AppWorkflows_Audit",
                    type: "char(36)",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                    collation: "ascii_general_ci",
                    oldClrType: typeof(Guid),
                    oldType: "char(36)",
                    oldNullable: true
                )
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "ParentStageId",
                    schema: "Application",
                    table: "AppWorkflows",
                    type: "char(36)",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                    collation: "ascii_general_ci",
                    oldClrType: typeof(Guid),
                    oldType: "char(36)",
                    oldNullable: true
                )
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci"
            );

            migrationBuilder.AddColumn<int>(
                name: "WorkflowRole",
                schema: "Application",
                table: "ApplicationOfficers_Audit",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "WorkflowRole",
                schema: "Application",
                table: "ApplicationOfficers",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_ApplicationOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "ApplicationOfficerId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_AppWorkflowLevelStages_ApplicationOfficers_ApplicationOffice~",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "ApplicationOfficerId",
                principalSchema: "Application",
                principalTable: "ApplicationOfficers",
                principalColumn: "ApplicationOfficerId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowLevelStagePrivileges_WorkflowLevelStages_WorkflowLev~",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                column: "WorkflowLevelId",
                principalSchema: "Workflow",
                principalTable: "WorkflowLevelStages",
                principalColumn: "WorkflowLevelStageId",
                onDelete: ReferentialAction.Cascade
            );
        }
    }
}
