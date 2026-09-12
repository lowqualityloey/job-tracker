using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationOwnerExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "applications",
                type: "uuid",
                nullable: true);
            // AC-9's expand stage: add the column nullable, then backfill. Tolerant by design — if there are no users
            // yet, the subquery yields NULL and rows simply stay unowned; the *contract* migration is what refuses.
            // Putting the refusal here would make a failed upgrade roll back a column that was safe to add, and would
            // split the "assertion before the constraint" rule across two files.
            //
            // Deterministic pick (earliest created_at), not `random()`: a backfill that is not reproducible cannot be
            // argued about, and on a single-owner database (ASSUMPTION-002) the choice only matters if someone later
            // runs it against a multi-user dump -- where "the first account owns everything" is at least auditable.
            migrationBuilder.Sql(
                "update applications set owner_id = (select id from users order by created_at limit 1) "
                + "where owner_id is null;");

            migrationBuilder.CreateIndex(
                name: "applications_owner_id_idx",
                table: "applications",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "applications_owner_id_idx",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "applications");
        }
    }
}
