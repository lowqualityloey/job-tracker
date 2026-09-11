using JobTracker.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace JobTracker.Api;

/// <summary>
/// Spec §4.1's deep module: everything the API knows about applications — routes, handlers, and the shape it
/// accepts on write — and the only place that knows it. Program.cs is composition (middleware, DI, startup) and
/// nothing else, so the question "what does this API do?" has exactly one file to answer it.
///
/// Extracted after BEHAVIOR-…-034 produced the second problem-document factory, which was the trigger recorded in
/// Slice 1's commit messages. Static methods over the DbContext rather than an injected service: §4.1 rejects
/// rebuilding the repository seam here, because the frontend already has one and two seams moving together is the
/// failure DECISION-m3-backend-api-005 exists to prevent.
/// </summary>
public static class ApplicationCatalog
{
    public static IEndpointRouteBuilder MapApplicationCatalog(this IEndpointRouteBuilder endpoints)
    {
                // BEHAVIOR-m3-backend-api-026. AsNoTracking because this list is never edited in place, and tracking it would
        // mean the read path keeps a change tracker alive for no reason. CancellationToken is bound by the framework to
        // the request aborting: a client that navigates away should stop the query rather than finish it into a socket
        // nobody reads.
        endpoints.MapGet("/api/applications", async (JobTrackerDb db, CancellationToken ct) =>
            await db.Applications.AsNoTracking().ToListAsync(ct));

        // BEHAVIOR-m3-backend-api-028. The lambda's inferred return type is Task<IResult>: both arms are results rather
        // than values, and letting the compiler arrive at that is clearer than annotating a union type by hand.
        endpoints.MapGet("/api/applications/{id}", async (string id, JobTrackerDb db, CancellationToken ct) =>
        {
            // Guid.TryParse rather than a {id:guid} route constraint: a malformed id is the same fact to the client
            // ("there is no such record") and must not leave the endpoint answering with a framework envelope that has
            // no `code` in it. See 028's Red commit for the measured body of that default.
            if (!Guid.TryParse(id, out var guid))
            {
                return Problems.NotFound(id);
            }

            var application = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == guid, ct);
            return application is null ? Problems.NotFound(id) : Results.Ok(application);
        });

        // BEHAVIOR-m3-backend-api-030. Deliberately permissive about *values*: the status CHECK is enforced by the
        // database (032), and the field-by-field validator is 031's registered subject, which brings the shared fixture
        // with it. What this handler must do is refuse nothing the schema would accept, and return the row the client
        // needs — including `revision`, which EF populates on save because xmin is a store-generated concurrency token.
        endpoints.MapPost("/api/applications", async (NewApplicationRequest request, JobTrackerDb db, CancellationToken ct) =>
        {
            var (errors, value) = ApplicationValidation.Validate(request);
            if (errors.Count > 0)
            {
                return Problems.Validation(errors, "/api/applications");
            }

            var entity = new Application
            {
                Id = value!.Id,
                CompanyName = value.CompanyName,
                JobTitle = value.JobTitle,
                Location = value.Location,
                Status = value.Status,
                AppliedAt = value.AppliedAt,
                Notes = value.Notes,
                // Left at default on purpose: created_at/updated_at are the database's now(), and a client-supplied
                // creation time would let a retry claim the record is older than it is.
            };

            db.Applications.Add(entity);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // BEHAVIOR-m3-backend-api-034. The id is client-minted (DECISION-m3-backend-api-007), so the only thing
                // that distinguishes "create this" from "I already sent you this" is the primary key. Letting it reach the
                // exception handler would surface a duplicate as `storage-error` — the client's mapping has no other word
                // for a 500 — and the user would be told storage failed about a record that saved fine on the first try.
                return Problems.Conflict(request.Id);
            }

            // Results.Created rather than Ok: the 201 is what the client's adapter distinguishes a fresh create from, and
            // the Location header is the canonical path 033/044 will address with If-Match.
            return Results.Created($"/api/applications/{entity.Id}", entity);
        });
        // BEHAVIOR-m3-backend-api-033. §4.3 spells PUT as a *full replacement* with If-Match required, and the
        // 428 it names for a missing precondition is mapped to 409 by that same table — which falls out for free
        // here, because IsCurrent refuses an absent header exactly as it refuses a stale one. No branch of its own
        // to write, and no branch that could rot untested.
        endpoints.MapPut("/api/applications/{id}", async (string id, NewApplicationRequest request, JobTrackerDb db,
            CancellationToken ct, [FromHeader(Name = "If-Match")] string? ifMatch) =>
        {
            // The path is authoritative for identity and the body's id is ignored. Nothing in the register covers
            // a mismatch between the two; a fourth §4.3 statement with no behaviour. Documented rather than
            // invented into a 400 nobody has asked for.
            if (!Guid.TryParse(id, out var guid))
            {
                return Problems.NotFound(id);
            }

            // Validated before the row is even looked up, and through the same call the create path makes: two
            // verbs each keeping their own copy of the rules is how one of them starts accepting records the other
            // rejects. §4.3 lists `400 validation` for both, and this is the whole reason the validator is a
            // function rather than a few lines inside the POST handler.
            var (errors, value) = ApplicationValidation.Validate(request);
            if (errors.Count > 0)
            {
                return Problems.Validation(errors, $"/api/applications/{id}");
            }

            var entity = await db.Applications.FirstOrDefaultAsync(a => a.Id == guid, ct);
            if (entity is null)
            {
                return Problems.NotFound(id);
            }

            if (!IsCurrent(ifMatch, entity.Revision))
            {
                return Problems.Conflict(guid);
            }

            // Assigned field by field rather than attaching a detached entity, because an update that overwrote
            // id, revision, created_at or updated_at from a client payload would be a silent data-loss bug in the
            // one operation whose whole purpose is editing a record.
            entity.CompanyName = value!.CompanyName;
            entity.JobTitle = value.JobTitle;
            entity.Location = value.Location;
            entity.Status = value.Status;
            entity.AppliedAt = value.AppliedAt;
            entity.Notes = value.Notes;

            await db.SaveChangesAsync(ct);
            return Results.Ok(entity);
        });

        // BEHAVIOR-m3-backend-api-035. Tracks the entity (no AsNoTracking) because it is being removed, and
        // returns the same 404 as the read path: "no such record" is one fact whether it surfaces on GET or DELETE.
        //
        // If-Match is ignored here, which is a known defect and not an oversight — 044's Red turns it into a
        // failing test first. See that commit's message for why a minimal 035 Green was written blind to it.
        endpoints.MapDelete("/api/applications/{id}", async (string id, JobTrackerDb db, CancellationToken ct, [FromHeader(Name = "If-Match")] string? ifMatch) =>
        {
            if (!Guid.TryParse(id, out var guid))
            {
                return Problems.NotFound(id);
            }

            var entity = await db.Applications.FirstOrDefaultAsync(a => a.Id == guid, ct);
            if (entity is null)
            {
                return Problems.NotFound(id);
            }

            // BEHAVIOR-m3-backend-api-044 (grill F-3: If-Match is required on DELETE). Absent, unparseable or
            // mismatched all refuse the write, because none of them *proves* the caller is looking at the current
            // row — and a delete is the one operation in this API whose damage a retry cannot undo.
            if (!IsCurrent(ifMatch, entity.Revision))
            {
                return Problems.Conflict(guid);
            }

            db.Applications.Remove(entity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return endpoints;
    }

    /// <summary>
    /// Does this <c>If-Match</c> value name the row's current version?
    ///
    /// Quoted, because §4.3 spells the token as `ETag: "&lt;xmin&gt;"` and a client that echoes a header verbatim sends
    /// the quotes; an implementation that compared raw digits would pass a test written in the same file and fail
    /// against a browser. <c>W/</c> (weak) is deliberately *not* accepted: a weak validator explicitly does not
    /// guarantee byte-equivalence, which is the entire reason a precondition on a destroy exists. <c>*</c> ("any
    /// version") is likewise refused rather than honoured — no behaviour in M3 asks for it, and silently accepting
    /// it would be a hole a future client could fall into without a failing test first.
    /// </summary>
    private static bool IsCurrent(string? ifMatch, uint revision)
    {
        if (ifMatch is null)
        {
            return false;
        }

        var candidate = ifMatch.Trim();
        if (candidate.StartsWith("W/", StringComparison.Ordinal))
        {
            return false;
        }

        candidate = candidate.Trim('"');
        return uint.TryParse(candidate, out var value) && value == revision;
    }
}
