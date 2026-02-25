using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AccountDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "Companies");

            migrationBuilder
                .CreateTable(
                    name: "Companies",
                    schema: "Companies",
                    columns: table => new
                    {
                        CompanyId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        UenNumber = table
                            .Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
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
                        table.PrimaryKey("PK_Companies", x => x.CompanyId);
                        table.ForeignKey(
                            name: "FK_Companies_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_Companies_Accounts_LastModifiedById",
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
                    name: "Companies_Audit",
                    schema: "Companies",
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
                        CompanyId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        UenNumber = table
                            .Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4")
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Companies_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_Companies_Audit_Accounts_AuditActionById",
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
                    name: "MedicsAccountRoles_Audit",
                    schema: "Auth",
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
                        MedicsAccountRoleId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        MedicsRoleId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_MedicsAccountRoles_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_MedicsAccountRoles_Audit_Accounts_AuditActionById",
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
                    name: "MedicsRoles",
                    schema: "Auth",
                    columns: table => new
                    {
                        MedicsRoleId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        RoleType = table.Column<int>(type: "int", nullable: false),
                        IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        LastModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_MedicsRoles", x => x.MedicsRoleId);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "MopProfileCompanies_Audit",
                    schema: "Companies",
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
                        MopProfileCompanyId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        MopProfileId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        CompanyId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        )
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_MopProfileCompanies_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_MopProfileCompanies_Audit_Accounts_AuditActionById",
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
                    name: "MopProfiles",
                    schema: "Auth",
                    columns: table => new
                    {
                        MopProfileId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Email = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
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
                        table.PrimaryKey("PK_MopProfiles", x => x.MopProfileId);
                        table.ForeignKey(
                            name: "FK_MopProfiles_Accounts_AccountId",
                            column: x => x.AccountId,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_MopProfiles_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_MopProfiles_Accounts_LastModifiedById",
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
                    name: "MopProfiles_Audit",
                    schema: "Auth",
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
                        MopProfileId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Email = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4")
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_MopProfiles_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_MopProfiles_Audit_Accounts_AuditActionById",
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
                    name: "OfficerProfiles",
                    schema: "Auth",
                    columns: table => new
                    {
                        OfficerProfileId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Email = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Status = table.Column<int>(type: "int", nullable: false),
                        Designation = table
                            .Column<string>(type: "longtext", nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        SoeId = table
                            .Column<string>(type: "longtext", nullable: true)
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
                        table.PrimaryKey("PK_OfficerProfiles", x => x.OfficerProfileId);
                        table.ForeignKey(
                            name: "FK_OfficerProfiles_Accounts_AccountId",
                            column: x => x.AccountId,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_OfficerProfiles_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_OfficerProfiles_Accounts_LastModifiedById",
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
                    name: "OfficerProfiles_Audit",
                    schema: "Auth",
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
                        OfficerProfileId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Email = table
                            .Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Status = table.Column<int>(type: "int", nullable: false),
                        Designation = table
                            .Column<string>(type: "longtext", nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        SoeId = table
                            .Column<string>(type: "longtext", nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4")
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OfficerProfiles_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_OfficerProfiles_Audit_Accounts_AuditActionById",
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
                    name: "MedicsAccountRoles",
                    schema: "Auth",
                    columns: table => new
                    {
                        MedicsAccountRoleId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        MedicsRoleId = table.Column<Guid>(
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
                        table.PrimaryKey("PK_MedicsAccountRoles", x => x.MedicsAccountRoleId);
                        table.ForeignKey(
                            name: "FK_MedicsAccountRoles_Accounts_AccountId",
                            column: x => x.AccountId,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_MedicsAccountRoles_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Cascade
                        );
                        table.ForeignKey(
                            name: "FK_MedicsAccountRoles_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId"
                        );
                        table.ForeignKey(
                            name: "FK_MedicsAccountRoles_MedicsRoles_MedicsRoleId",
                            column: x => x.MedicsRoleId,
                            principalSchema: "Auth",
                            principalTable: "MedicsRoles",
                            principalColumn: "MedicsRoleId",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "MopProfileCompanies",
                    schema: "Companies",
                    columns: table => new
                    {
                        MopProfileCompanyId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        MopProfileId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        CompanyId = table.Column<Guid>(
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
                        table.PrimaryKey("PK_MopProfileCompanies", x => x.MopProfileCompanyId);
                        table.ForeignKey(
                            name: "FK_MopProfileCompanies_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_MopProfileCompanies_Accounts_LastModifiedById",
                            column: x => x.LastModifiedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_MopProfileCompanies_Companies_CompanyId",
                            column: x => x.CompanyId,
                            principalSchema: "Companies",
                            principalTable: "Companies",
                            principalColumn: "CompanyId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_MopProfileCompanies_MopProfiles_MopProfileId",
                            column: x => x.MopProfileId,
                            principalSchema: "Auth",
                            principalTable: "MopProfiles",
                            principalColumn: "MopProfileId",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CreatedById",
                schema: "Companies",
                table: "Companies",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Companies_LastModifiedById",
                schema: "Companies",
                table: "Companies",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Companies_UenNumber",
                schema: "Companies",
                table: "Companies",
                column: "UenNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Audit_AuditActionById",
                schema: "Companies",
                table: "Companies_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MedicsAccountRoles_AccountId",
                schema: "Auth",
                table: "MedicsAccountRoles",
                column: "AccountId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MedicsAccountRoles_CreatedById",
                schema: "Auth",
                table: "MedicsAccountRoles",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MedicsAccountRoles_LastModifiedById",
                schema: "Auth",
                table: "MedicsAccountRoles",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MedicsAccountRoles_MedicsRoleId",
                schema: "Auth",
                table: "MedicsAccountRoles",
                column: "MedicsRoleId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MedicsAccountRoles_Audit_AuditActionById",
                schema: "Auth",
                table: "MedicsAccountRoles_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfileCompanies_CompanyId",
                schema: "Companies",
                table: "MopProfileCompanies",
                column: "CompanyId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfileCompanies_CreatedById",
                schema: "Companies",
                table: "MopProfileCompanies",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfileCompanies_LastModifiedById",
                schema: "Companies",
                table: "MopProfileCompanies",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfileCompanies_MopProfileId",
                schema: "Companies",
                table: "MopProfileCompanies",
                column: "MopProfileId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfileCompanies_Audit_AuditActionById",
                schema: "Companies",
                table: "MopProfileCompanies_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfiles_AccountId",
                schema: "Auth",
                table: "MopProfiles",
                column: "AccountId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfiles_CreatedById",
                schema: "Auth",
                table: "MopProfiles",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfiles_LastModifiedById",
                schema: "Auth",
                table: "MopProfiles",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MopProfiles_Audit_AuditActionById",
                schema: "Auth",
                table: "MopProfiles_Audit",
                column: "AuditActionById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OfficerProfiles_AccountId",
                schema: "Auth",
                table: "OfficerProfiles",
                column: "AccountId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OfficerProfiles_CreatedById",
                schema: "Auth",
                table: "OfficerProfiles",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OfficerProfiles_LastModifiedById",
                schema: "Auth",
                table: "OfficerProfiles",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OfficerProfiles_Audit_AuditActionById",
                schema: "Auth",
                table: "OfficerProfiles_Audit",
                column: "AuditActionById"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Companies_Audit", schema: "Companies");

            migrationBuilder.DropTable(name: "MedicsAccountRoles", schema: "Auth");

            migrationBuilder.DropTable(name: "MedicsAccountRoles_Audit", schema: "Auth");

            migrationBuilder.DropTable(name: "MopProfileCompanies", schema: "Companies");

            migrationBuilder.DropTable(name: "MopProfileCompanies_Audit", schema: "Companies");

            migrationBuilder.DropTable(name: "MopProfiles_Audit", schema: "Auth");

            migrationBuilder.DropTable(name: "OfficerProfiles", schema: "Auth");

            migrationBuilder.DropTable(name: "OfficerProfiles_Audit", schema: "Auth");

            migrationBuilder.DropTable(name: "MedicsRoles", schema: "Auth");

            migrationBuilder.DropTable(name: "Companies", schema: "Companies");

            migrationBuilder.DropTable(name: "MopProfiles", schema: "Auth");
        }
    }
}
