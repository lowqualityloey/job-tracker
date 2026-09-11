using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropPlaceholderTextDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-filled, and necessarily: `migrations add` produced an empty Up() because the model snapshot
            // already says "no default" for these columns. The snapshot describes an intention; the database
            // carries three `DEFAULT ''` placeholders that EF wrote when it added NOT NULL columns to a populated
            // table and never removed. migrationBuilder.Sql is the sanctioned way to say what the model cannot.
            //
            // `drop default` rather than `alter column ... set not null`: the NOT NULL is already there and
            // already correct. This changes only what happens when a column is *omitted*, from "silently stored as
            // the empty string" to "23502, no rows affected" — which is what makes the mismatch in
            // §4.1's `text NOT NULL` worth a migration at all.
            foreach (var column in new[] { "company_name", "job_title", "status" })
            {
                migrationBuilder.Sql(
                    $"alter table \"applications\" alter column \"{column}\" drop default;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric on purpose. The rollback is safe only because 031's validator is the layer that keeps
            // empty strings out; restoring the defaults puts the silent-blank-status hazard back, which is the
            // pre-M3 behaviour and exactly what a rollback is meant to reproduce.
            foreach (var column in new[] { "company_name", "job_title", "status" })
            {
                migrationBuilder.Sql(
                    $"alter table \"applications\" alter column \"{column}\" set default '';");
            }
        }
    }
}
