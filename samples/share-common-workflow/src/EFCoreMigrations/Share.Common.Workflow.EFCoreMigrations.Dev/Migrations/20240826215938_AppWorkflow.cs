using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AppWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "Application");

            migrationBuilder.EnsureSchema(name: "Workflow");

            migrationBuilder
                .CreateTable(
                    name: "ApplicationOfficers",
                    schema: "Application",
                    columns: table => new
                    {
                        ApplicationOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        OfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowRole = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_ApplicationOfficers", x => x.ApplicationOfficerId);
                        table.ForeignKey(
                            name: "FK_ApplicationOfficers_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_ApplicationOfficers_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_ApplicationOfficers_Accounts_OfficerId",
                            column: x => x.OfficerId,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "ApplicationOfficers_Audit",
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
                            .Column<string>(type: "varchar(34)", maxLength: 34, nullable: false)
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
                        ApplicationOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        OfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowRole = table.Column<int>(type: "int", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_ApplicationOfficers_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_ApplicationOfficers_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevels_Audit",
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
                            .Column<string>(type: "varchar(21)", maxLength: 21, nullable: false)
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
                        AppWorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AppWorkflowLevels_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevels_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStageActions_Audit",
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
                            .Column<string>(type: "varchar(34)", maxLength: 34, nullable: false)
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
                        AppWorkflowLevelStageActionId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Decision = table.Column<int>(type: "int", nullable: false),
                        AppOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Remarks = table
                            .Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        DecisionMadeAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AppWorkflowLevelStageActions_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActions_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStagePrivilegeActions_Audit",
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
                        AppWorkflowLevelStagePrivilegeActionId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ConditionType = table.Column<int>(type: "int", nullable: false),
                        CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        Remarks = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ApplicationWorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey(
                            "PK_AppWorkflowLevelStagePrivilegeActions_Audit",
                            x => x.AuditId
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStagePrivilegeActions_Audit_Accounts_AuditAc~",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStages_Audit",
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
                            .Column<string>(type: "varchar(34)", maxLength: 34, nullable: false)
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
                        AppWorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        AppWorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        StageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AppWorkflowLevelStages_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStages_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflows_Audit",
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
                            .Column<string>(type: "varchar(21)", maxLength: 21, nullable: false)
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
                        AppWorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ApplicationId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ParentStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Status = table.Column<int>(type: "int", nullable: false),
                        CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AppWorkflows_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_AppWorkflows_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "WorkflowLevels_Audit",
                    schema: "Workflow",
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
                            .Column<string>(type: "varchar(21)", maxLength: 21, nullable: false)
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
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Level = table.Column<int>(type: "int", nullable: false),
                        IsFinal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        WorkflowRole = table.Column<int>(type: "int", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_WorkflowLevels_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_WorkflowLevels_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "WorkflowLevelStagePrivileges_Audit",
                    schema: "Workflow",
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
                            .Column<string>(type: "varchar(34)", maxLength: 34, nullable: false)
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
                        WorkflowLevelStagePrivilegeId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        PrivilegeType = table.Column<int>(type: "int", nullable: false),
                        IsRequiredToFinalizeLevel = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_WorkflowLevelStagePrivileges_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStagePrivileges_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "WorkflowLevelStages_Audit",
                    schema: "Workflow",
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
                            .Column<string>(type: "varchar(34)", maxLength: 34, nullable: false)
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
                        WorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Stage = table.Column<int>(type: "int", nullable: false),
                        Name = table
                            .Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4")
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_WorkflowLevelStages_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStages_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Workflows",
                    schema: "Workflow",
                    columns: table => new
                    {
                        WorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        WorkflowType = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_Workflows", x => x.WorkflowId);
                        table.ForeignKey(
                            name: "FK_Workflows_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_Workflows_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Workflows_Audit",
                    schema: "Workflow",
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
                            .Column<string>(type: "varchar(13)", maxLength: 13, nullable: false)
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
                        WorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        WorkflowType = table.Column<int>(type: "int", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Workflows_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_Workflows_Audit_Accounts_AuditActionById",
                            column: x => x.AuditActionById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "WorkflowLevels",
                    schema: "Workflow",
                    columns: table => new
                    {
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Level = table.Column<int>(type: "int", nullable: false),
                        IsFinal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        WorkflowRole = table.Column<int>(type: "int", nullable: false),
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
                        table.PrimaryKey("PK_WorkflowLevels", x => x.WorkflowLevelId);
                        table.ForeignKey(
                            name: "FK_WorkflowLevels_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevels_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevels_Workflows_WorkflowId",
                            column: x => x.WorkflowId,
                            principalSchema: "Workflow",
                            principalTable: "Workflows",
                            principalColumn: "WorkflowId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "WorkflowLevelStages",
                    schema: "Workflow",
                    columns: table => new
                    {
                        WorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Stage = table.Column<int>(type: "int", nullable: false),
                        Name = table
                            .Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
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
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_WorkflowLevelStages", x => x.WorkflowLevelStageId);
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStages_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStages_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStages_WorkflowLevels_WorkflowLevelId",
                            column: x => x.WorkflowLevelId,
                            principalSchema: "Workflow",
                            principalTable: "WorkflowLevels",
                            principalColumn: "WorkflowLevelId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "WorkflowLevelStagePrivileges",
                    schema: "Workflow",
                    columns: table => new
                    {
                        WorkflowLevelStagePrivilegeId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        PrivilegeType = table.Column<int>(type: "int", nullable: false),
                        IsRequiredToFinalizeLevel = table.Column<bool>(
                            type: "tinyint(1)",
                            nullable: false
                        ),
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
                            "PK_WorkflowLevelStagePrivileges",
                            x => x.WorkflowLevelStagePrivilegeId
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStagePrivileges_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStagePrivileges_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_WorkflowLevelStagePrivileges_WorkflowLevelStages_WorkflowLev~",
                            column: x => x.WorkflowLevelId,
                            principalSchema: "Workflow",
                            principalTable: "WorkflowLevelStages",
                            principalColumn: "WorkflowLevelStageId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevels",
                    schema: "Application",
                    columns: table => new
                    {
                        AppWorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                        table.PrimaryKey("PK_AppWorkflowLevels", x => x.AppWorkflowLevelId);
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevels_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevels_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevels_WorkflowLevels_WorkflowLevelId",
                            column: x => x.WorkflowLevelId,
                            principalSchema: "Workflow",
                            principalTable: "WorkflowLevels",
                            principalColumn: "WorkflowLevelId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStages",
                    schema: "Application",
                    columns: table => new
                    {
                        AppWorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        AppWorkflowLevelId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        StageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ApplicationOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: true,
                            collation: "ascii_general_ci"
                        ),
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
                            "PK_AppWorkflowLevelStages",
                            x => x.AppWorkflowLevelStageId
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStages_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStages_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStages_AppWorkflowLevels_AppWorkflowLevelId",
                            column: x => x.AppWorkflowLevelId,
                            principalSchema: "Application",
                            principalTable: "AppWorkflowLevels",
                            principalColumn: "AppWorkflowLevelId",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStages_ApplicationOfficers_ApplicationOffice~",
                            column: x => x.ApplicationOfficerId,
                            principalSchema: "Application",
                            principalTable: "ApplicationOfficers",
                            principalColumn: "ApplicationOfficerId"
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStages_WorkflowLevelStages_StageId",
                            column: x => x.StageId,
                            principalSchema: "Workflow",
                            principalTable: "WorkflowLevelStages",
                            principalColumn: "WorkflowLevelStageId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflowLevelStageActions",
                    schema: "Application",
                    columns: table => new
                    {
                        AppWorkflowLevelStageActionId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AppWorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Decision = table.Column<int>(type: "int", nullable: false),
                        AppOfficerId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Remarks = table
                            .Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        DecisionMadeAt = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
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
                            "PK_AppWorkflowLevelStageActions",
                            x => x.AppWorkflowLevelStageActionId
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActions_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActions_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActions_AppWorkflowLevelStages_AppWorkf~",
                            column: x => x.AppWorkflowLevelStageId,
                            principalSchema: "Application",
                            principalTable: "AppWorkflowLevelStages",
                            principalColumn: "AppWorkflowLevelStageId",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStageActions_ApplicationOfficers_AppOfficerId",
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
                    name: "AppWorkflowLevelStagePrivilegeActions",
                    schema: "Application",
                    columns: table => new
                    {
                        AppWorkflowLevelStagePrivilegeActionId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ConditionType = table.Column<int>(type: "int", nullable: false),
                        CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        Remarks = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ApplicationWorkflowLevelStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
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
                            "PK_AppWorkflowLevelStagePrivilegeActions",
                            x => x.AppWorkflowLevelStagePrivilegeActionId
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStagePrivilegeActions_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStagePrivilegeActions_Accounts_LastModifiedB~",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflowLevelStagePrivilegeActions_AppWorkflowLevelStages~",
                            column: x => x.ApplicationWorkflowLevelStageId,
                            principalSchema: "Application",
                            principalTable: "AppWorkflowLevelStages",
                            principalColumn: "AppWorkflowLevelStageId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AppWorkflows",
                    schema: "Application",
                    columns: table => new
                    {
                        AppWorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ApplicationId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        WorkflowId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ParentStageId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Status = table.Column<int>(type: "int", nullable: false),
                        CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                        table.PrimaryKey("PK_AppWorkflows", x => x.AppWorkflowId);
                        table.ForeignKey(
                            name: "FK_AppWorkflows_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflows_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflows_AppWorkflowLevelStages_ParentStageId",
                            column: x => x.ParentStageId,
                            principalSchema: "Application",
                            principalTable: "AppWorkflowLevelStages",
                            principalColumn: "AppWorkflowLevelStageId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_AppWorkflows_Workflows_WorkflowId",
                            column: x => x.WorkflowId,
                            principalSchema: "Workflow",
                            principalTable: "Workflows",
                            principalColumn: "WorkflowId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationOfficers_CreatedById",
                schema: "Application",
                table: "ApplicationOfficers",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationOfficers_LastModifiedById",
                schema: "Application",
                table: "ApplicationOfficers",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationOfficers_OfficerId",
                schema: "Application",
                table: "ApplicationOfficers",
                column: "OfficerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationOfficers_Audit_AuditActionById",
                schema: "Application",
                table: "ApplicationOfficers_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevels_AppWorkflowId",
                schema: "Application",
                table: "AppWorkflowLevels",
                column: "AppWorkflowId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevels_CreatedById",
                schema: "Application",
                table: "AppWorkflowLevels",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevels_LastModifiedById",
                schema: "Application",
                table: "AppWorkflowLevels",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevels_WorkflowLevelId",
                schema: "Application",
                table: "AppWorkflowLevels",
                column: "WorkflowLevelId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevels_Audit_AuditActionById",
                schema: "Application",
                table: "AppWorkflowLevels_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActions_AppOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStageActions",
                column: "AppOfficerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActions_AppWorkflowLevelStageId",
                schema: "Application",
                table: "AppWorkflowLevelStageActions",
                column: "AppWorkflowLevelStageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActions_CreatedById",
                schema: "Application",
                table: "AppWorkflowLevelStageActions",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActions_LastModifiedById",
                schema: "Application",
                table: "AppWorkflowLevelStageActions",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStageActions_Audit_AuditActionById",
                schema: "Application",
                table: "AppWorkflowLevelStageActions_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStagePrivilegeActions_ApplicationWorkflowLev~",
                schema: "Application",
                table: "AppWorkflowLevelStagePrivilegeActions",
                column: "ApplicationWorkflowLevelStageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStagePrivilegeActions_CreatedById",
                schema: "Application",
                table: "AppWorkflowLevelStagePrivilegeActions",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStagePrivilegeActions_LastModifiedById",
                schema: "Application",
                table: "AppWorkflowLevelStagePrivilegeActions",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStagePrivilegeActions_Audit_AuditActionById",
                schema: "Application",
                table: "AppWorkflowLevelStagePrivilegeActions_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_ApplicationOfficerId",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "ApplicationOfficerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_AppWorkflowLevelId",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "AppWorkflowLevelId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_CreatedById",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_LastModifiedById",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_StageId",
                schema: "Application",
                table: "AppWorkflowLevelStages",
                column: "StageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflowLevelStages_Audit_AuditActionById",
                schema: "Application",
                table: "AppWorkflowLevelStages_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflows_CreatedById",
                schema: "Application",
                table: "AppWorkflows",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflows_LastModifiedById",
                schema: "Application",
                table: "AppWorkflows",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflows_ParentStageId",
                schema: "Application",
                table: "AppWorkflows",
                column: "ParentStageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflows_WorkflowId",
                schema: "Application",
                table: "AppWorkflows",
                column: "WorkflowId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppWorkflows_Audit_AuditActionById",
                schema: "Application",
                table: "AppWorkflows_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevels_CreatedById",
                schema: "Workflow",
                table: "WorkflowLevels",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevels_LastModifiedById",
                schema: "Workflow",
                table: "WorkflowLevels",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevels_WorkflowId",
                schema: "Workflow",
                table: "WorkflowLevels",
                column: "WorkflowId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevels_Audit_AuditActionById",
                schema: "Workflow",
                table: "WorkflowLevels_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStagePrivileges_CreatedById",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStagePrivileges_LastModifiedById",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStagePrivileges_WorkflowLevelId",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges",
                column: "WorkflowLevelId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStagePrivileges_Audit_AuditActionById",
                schema: "Workflow",
                table: "WorkflowLevelStagePrivileges_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStages_CreatedById",
                schema: "Workflow",
                table: "WorkflowLevelStages",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStages_LastModifiedById",
                schema: "Workflow",
                table: "WorkflowLevelStages",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStages_WorkflowLevelId",
                schema: "Workflow",
                table: "WorkflowLevelStages",
                column: "WorkflowLevelId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowLevelStages_Audit_AuditActionById",
                schema: "Workflow",
                table: "WorkflowLevelStages_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_CreatedById",
                schema: "Workflow",
                table: "Workflows",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_LastModifiedById",
                schema: "Workflow",
                table: "Workflows",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_Audit_AuditActionById",
                schema: "Workflow",
                table: "Workflows_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_AppWorkflowLevels_AppWorkflows_AppWorkflowId",
                schema: "Application",
                table: "AppWorkflowLevels",
                column: "AppWorkflowId",
                principalSchema: "Application",
                principalTable: "AppWorkflows",
                principalColumn: "AppWorkflowId",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppWorkflowLevels_AppWorkflows_AppWorkflowId",
                schema: "Application",
                table: "AppWorkflowLevels"
            );

            migrationBuilder.DropTable(name: "ApplicationOfficers_Audit", schema: "Application");

            migrationBuilder.DropTable(name: "AppWorkflowLevels_Audit", schema: "Application");

            migrationBuilder.DropTable(name: "AppWorkflowLevelStageActions", schema: "Application");

            migrationBuilder.DropTable(
                name: "AppWorkflowLevelStageActions_Audit",
                schema: "Application"
            );

            migrationBuilder.DropTable(
                name: "AppWorkflowLevelStagePrivilegeActions",
                schema: "Application"
            );

            migrationBuilder.DropTable(
                name: "AppWorkflowLevelStagePrivilegeActions_Audit",
                schema: "Application"
            );

            migrationBuilder.DropTable(name: "AppWorkflowLevelStages_Audit", schema: "Application");

            migrationBuilder.DropTable(name: "AppWorkflows_Audit", schema: "Application");

            migrationBuilder.DropTable(name: "WorkflowLevels_Audit", schema: "Workflow");

            migrationBuilder.DropTable(name: "WorkflowLevelStagePrivileges", schema: "Workflow");

            migrationBuilder.DropTable(
                name: "WorkflowLevelStagePrivileges_Audit",
                schema: "Workflow"
            );

            migrationBuilder.DropTable(name: "WorkflowLevelStages_Audit", schema: "Workflow");

            migrationBuilder.DropTable(name: "Workflows_Audit", schema: "Workflow");

            migrationBuilder.DropTable(name: "AppWorkflows", schema: "Application");

            migrationBuilder.DropTable(name: "AppWorkflowLevelStages", schema: "Application");

            migrationBuilder.DropTable(name: "AppWorkflowLevels", schema: "Application");

            migrationBuilder.DropTable(name: "ApplicationOfficers", schema: "Application");

            migrationBuilder.DropTable(name: "WorkflowLevelStages", schema: "Workflow");

            migrationBuilder.DropTable(name: "WorkflowLevels", schema: "Workflow");

            migrationBuilder.DropTable(name: "Workflows", schema: "Workflow");
        }
    }
}
