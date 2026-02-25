using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AddAppWorkflowLevelStageActionRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStageActionRemarks",
                    schema: "Application",
                    columns: table => new
                    {
                        AppWorkflowLevelStageActionRemarkId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ApplicationId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowLevelStageActionId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        Remarks = table
                            .Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        LastModifiedAt = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        CreatedById = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        LastModifiedById = table.Column<Guid>(
                            type: "char(36)",
                            nullable: true,
                            collation: "ascii_general_ci"
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey(
                            "PK_AppWorkflowLevelStageActionRemarks",
                            x => x.AppWorkflowLevelStageActionRemarkId
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActionRemarks_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActionRemarks_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActionRemarks_ApplicationOfficers_AppOf~",
                            column: x => x.AppOfficerId,
                            principalSchema: "Application",
                            principalTable: "ApplicationOfficers",
                            principalColumn: "ApplicationOfficerId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStageActionRemarks_Audit",
                    schema: "Application",
                    columns: table => new
                    {
                        AuditId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AuditTransactionId = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        AuditAction = table
                            .Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        AuditActionById = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AuditedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        Discriminator = table
                            .Column<string>(type: "varchar(55)", maxLength: 55, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        LastModifiedAt = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        CreatedById = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        LastModifiedById = table.Column<Guid>(
                            type: "char(36)",
                            nullable: true,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowLevelStageActionRemarkId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ApplicationId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowLevelStageActionId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        Remarks = table
                            .Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey(
                            "PK_AppWorkflowLevelStageActionRemarks_Audit",
                            x => x.AuditId
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActionRemarks_Audit_Accounts_AuditActio~",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActionRemarks_AppOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStageActionRemarks",
                column: "AppOfficerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActionRemarks_CreatedById",
                schema: "Application",
                table: "AppWorkflowLevelStageActionRemarks",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActionRemarks_LastModifiedById",
                schema: "Application",
                table: "AppWorkflowLevelStageActionRemarks",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActionRemarks_Audit_AuditActionById",
                schema: "Application",
                table: "AppWorkflowLevelStageActionRemarks_Audit",
                column: "AuditActionById"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppWorkflowLevelStageActionRemarks",
                schema: "Application"
            );

            migrationBuilder.DropTable(
                name: "AppWorkflowLevelStageActionRemarks_Audit",
                schema: "Application"
            );
        }
    }
}
