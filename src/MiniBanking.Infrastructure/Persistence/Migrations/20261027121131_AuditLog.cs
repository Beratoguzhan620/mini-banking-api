using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniBanking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_id",
                table: "audit_log",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_correlation_id",
                table: "audit_log",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_target_type_target_id",
                table: "audit_log",
                columns: new[] { "target_type", "target_id" });

            // The trail is evidence, so the database itself refuses to change or remove it.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_log_is_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log is append-only';
                END;
                $$;

                CREATE TRIGGER audit_log_no_update_or_delete
                    BEFORE UPDATE OR DELETE ON audit_log
                    FOR EACH ROW EXECUTE FUNCTION audit_log_is_append_only();

                CREATE TRIGGER audit_log_no_truncate
                    BEFORE TRUNCATE ON audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_log_is_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;
                DROP TRIGGER IF EXISTS audit_log_no_update_or_delete ON audit_log;
                DROP FUNCTION IF EXISTS audit_log_is_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log");
        }
    }
}
