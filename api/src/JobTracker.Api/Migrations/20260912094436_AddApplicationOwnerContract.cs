using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationOwnerContract : Migration
    {
        /// <summary>
        /// The pre-constraint guard's SQL, exposed so it can be <b>tested as a statement</b> instead of only as a step in a
        /// migration nobody can re-run without a second database. A test points it at a scratch table it controls: zero
        /// unowned rows must pass, one must raise. That is AC-9's "assertion before the constraint" verified directly
        /// rather than inferred from the fact that <c>Up()</c> did not throw.
        /// </summary>
        public static string AssertionSql(string table = "applications") => string.Format(GuardTemplate, table);

        private const string GuardTemplate =
            "do $$ "
            + "declare unowned integer; "
            + "begin "
            + "  select count(*) into unowned from {0} where owner_id is null; "
            + "  if unowned > 0 then "
            + "    raise exception 'AC-9 guard: {0} has % of rows with owner_id is null. The expand stage backfills "
            + "from the earliest account, so this means no users row existed to assign them to. Seed an account or assign "
            + "owners explicitly, then re-run the contract migration.', unowned; "
            + "  end if; "
            + "end "
            + "$$;" ;
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AC-9's rule, in the order it is written: assertion BEFORE constraint. Without it the database refuses an
            // un-nullable column that has values in it and the operator gets
            // `column "owner_id" of relation "applications" contains null values` -- true, but it does not say how many,
            // nor that the expand stage's backfill found no account to assign them to. This names the count and the fix.
            //
            // A guard, not a cleanup: silently assigning orphans to "the first user" here would make a data-ownership
            // decision as a side effect of whichever migration ran second. The expand stage already made that choice once,
            // in the one place where it can be defended.
            migrationBuilder.Sql(AssertionSql());

            migrationBuilder.AlterColumn<Guid>(
                name: "owner_id",
                table: "applications",
                type: "uuid",
                nullable: false,
                // EF emits `defaultValue: new Guid("0000...")` here whenever a non-nullable value type is added to a table
                // that has rows. Deleted on purpose: a database default of all-zeros IS the "owned by nobody" state -056
                // exists to make unreachable, and it makes the failure opaque -- an insert that forgets owner_id gets
                // 23503 (violates the FK to users) instead of 23502 (cannot be null). The loud constraint is the useful one.
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_applications_users_owner_id",
                table: "applications",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_applications_users_owner_id",
                table: "applications");

            migrationBuilder.AlterColumn<Guid>(
                name: "owner_id",
                table: "applications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
