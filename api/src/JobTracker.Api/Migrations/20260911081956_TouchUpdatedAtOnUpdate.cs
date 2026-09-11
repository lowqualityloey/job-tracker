using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class TouchUpdatedAtOnUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written DDL, and the only place in this project where that is correct. EF scaffolded an empty
            // Up() because the model has no vocabulary for triggers: `ValueGeneratedOnAddOrUpdate` on updated_at
            // describes *that* the store generates the value but not *how*, and the two annotations EF could
            // express — a computed column (`GENERATED ALWAYS AS`, which cannot call the non-immutable now()) or a
            // rowversion — are both wrong for a mutable timestamp. `migrationBuilder.Sql` is the intended escape
            // hatch for DDL the provider cannot model; pretending otherwise would mean dropping the trigger into a
            // later migration or setting the value in C# and contradicting spec §5 in the same breath.
            migrationBuilder.Sql(
                """
                create or replace function applications_touch_updated_at() returns trigger
                language plpgsql
                as $function$
                begin
                    -- now(), not clock_timestamp(): the same instant the transaction started, so every row written
                    -- by one request carries one timestamp and ordering stays reproducible.
                    new.updated_at := now();
                    return new;
                end;
                $function$;
                """);

            migrationBuilder.Sql(
                """
                create trigger applications_set_updated_at
                    before update on applications
                    for each row
                    execute function applications_touch_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Both halves, in dependency order: a function a trigger still references cannot be dropped, and a
            // Down() that throws is worse than a migration that never existed.
            migrationBuilder.Sql("drop trigger if exists applications_set_updated_at on applications;");
            migrationBuilder.Sql("drop function if exists applications_touch_updated_at();");
        }
    }
}
