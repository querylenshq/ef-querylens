using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "Auth");

            migrationBuilder.AlterDatabase().Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Accounts",
                    schema: "Auth",
                    columns: table => new
                    {
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "longtext", nullable: false)
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
                        table.PrimaryKey("PK_Accounts", x => x.AccountId);
                        table.ForeignKey(
                            name: "FK_Accounts_Accounts_CreatedById",
                            column: x => x.CreatedById,
                            principalSchema: "Auth",
                            principalTable: "Accounts",
                            principalColumn: "AccountId",
                            onDelete: ReferentialAction.Restrict
                        );
                        table.ForeignKey(
                            name: "FK_Accounts_Accounts_LastModifiedById",
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
                    name: "Accounts_Audit",
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
                        AccountId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "longtext", nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4")
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Accounts_Audit", x => x.AuditId);
                        table.ForeignKey(
                            name: "FK_Accounts_Audit_Accounts_AuditActionById",
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
                name: "IX_Accounts_CreatedById",
                schema: "Auth",
                table: "Accounts",
                column: "CreatedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_LastModifiedById",
                schema: "Auth",
                table: "Accounts",
                column: "LastModifiedById"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Audit_AuditActionById",
                schema: "Auth",
                table: "Accounts_Audit",
                column: "AuditActionById"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Accounts_Audit", schema: "Auth");

            migrationBuilder.DropTable(name: "Accounts", schema: "Auth");
        }
    }
}
