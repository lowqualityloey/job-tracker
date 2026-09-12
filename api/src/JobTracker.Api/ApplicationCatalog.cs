using JobTracker.Api.Auth;
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
        endpoints.MapGet("/api/applications", async (JobTrackerDb db, HttpContext http, CancellationToken ct) =>
        {
            // BEHAVIOR-056 / spec 2.2: the list is scoped in SQL, not filtered after the fetch. `WHERE owner_id = @me` is
            // one round trip and one index probe (-059 asserts the scan); `.ToList().Where(...)` would ship the whole
            // table to the process and then decline to show it, which is a leak with a UI stapled on.
            // RequireUserId is hoisted because EF translates the lambda: calling it inline asks Npgsql to interpret a
            // .NET method, and the failure would surface at query time on a route no -056 test touches.
            var owner = SessionGate.RequireUserId(http);
            return await db.Applications.AsNoTracking()
                .Where(a => a.OwnerId == owner)
                .ToListAsync(ct);
        });

        // BEHAVIOR-m3-backend-api-028. The lambda's inferred return type is Task<IResult>: both arms are results rather
        // than values, and letting the compiler arrive at that is clearer than annotating a union type by hand.
        endpoints.MapGet("/api/applications/{id}", async (string id, JobTrackerDb db, HttpContext http, CancellationToken ct) =>
        {
            // Guid.TryParse rather than a {id:guid} route constraint: a malformed id is the same fact to the client
            // ("there is no such record") and must not leave the endpoint answering with a framework envelope that has
            // no `code` in it. See 028's Red commit for the measured body of that default.
            if (!Guid.TryParse(id, out var guid))
            {
                return Problems.NotFound(id);
            }

                        var owner = SessionGate.RequireUserId(http);
            // The 404-not-403 rule lives here. An unscoped read that then compared owners would have to answer 403 to
            // "right id, wrong account", and 403 is an existence oracle. One composite predicate makes another account's
            // row indistinguishable from a row that never existed -- which is exactly what -056 asks for.
            var application = await db.Applications.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == guid && a.OwnerId == owner, ct);
            return application is null ? Problems.NotFound(id) : Results.Ok(application);
        });

        // BEHAVIOR-m3-backend-api-030. Deliberately permissive about *values*: the status CHECK is enforced by the
        // database (032), and the field-by-field validator is 031's registered subject, which brings the shared fixture
        // with it. What this handler must do is refuse nothing the schema would accept, and return the row the client
        // needs — including `revision`, which EF populates on save because xmin is a store-generated concurrency token.
        // BEHAVIOR-m3-backend-api-046 (spec DECISION-m3-backend-api-005, §4.3's sixth row). Registered ahead of
        // `/api/applications/{id}` for legibility rather than correctness: ASP.NET Core scores literal segments
        // above parameters, so "events" was never going to be looked up as an application id — but a reader who does
        // not know that should not have to find out by experiment, and the test asserts the media type for the same
        // reason.
        endpoints.MapGet("/api/applications/events", async (HttpContext http, ApplicationEventBus bus,
            CancellationToken ct) =>
        {
            // Headers set directly rather than negotiated: `EventSource` will not move out of CONNECTING until the
            // response starts streaming, and a buffered JSON-shaped start would leave every client silently
            // "connecting" while the user looks at a stale list. `no-cache` is not decoration either — a proxy that
            // considered an SSE response cacheable would serve one client's stream to another.
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";

            var changes = bus.Subscribe();
            try
            {
                // A comment frame, which SSE defines as ignorable and clients never dispatch. It exists to put bytes
                // on the wire the moment the connection opens, and it is what the "a dropped stream reconnects" row in
                // §4.3 depends on: without an initial flush the client's `open` event waits for the first write, so a
                // quiet catalog means a client that never learns it is connected.
                await http.Response.WriteAsync(": open\n\n", System.Text.Encoding.UTF8, ct);

                // `event: change` and not the default `message`: the adapter listens for a named event, so a frame
                // that carries a correct id and the wrong event name is delivered to no one. Both lines and the
                // terminating blank line are the wire format, not formatting.
                await foreach (var id in changes.Reader.ReadAllAsync(ct))
                {
                    await http.Response.WriteAsync($"event: change\ndata: {{\"id\":\"{id}\"}}\n\n",
                        System.Text.Encoding.UTF8, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // The browser navigated away, closed the tab, or dropped the connection — the normal end of an event
                // stream. `ct` is the request's cancellation token, so this is also how the endpoint stops holding a
                // Kestrel thread when a client goes silent.
            }
            finally
            {
                bus.Unsubscribe(changes);
            }
        });

        endpoints.MapPost("/api/applications", async (HttpContext http, NewApplicationRequest request, JobTrackerDb db, ApplicationEventBus bus, CancellationToken ct) =>
        {
            var (errors, value) = ApplicationValidation.Validate(request);
            if (errors.Count > 0)
            {
                return Problems.Validation(errors, "/api/applications");
            }

            var entity = new Application
            {
                Id = value!.Id,
                // Owned at birth. There is deliberately no path that creates an unowned row: -056's claim that B never
                // sees A's list only means something if "nobody's" is not a state rows can be in.
                OwnerId = SessionGate.RequireUserId(http),
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
                // value!.Id rather than request.Id: -057 widened the wire member to string?, and the compiler pointed
                // straight at this line. The duplicate-key conflict is about the row, which only exists once the id has
                // parsed -- so the validated value is the correct thing to name here, not the raw text.
                return Problems.Conflict(value!.Id);
            }

            // Results.Created rather than Ok: the 201 is what the client's adapter distinguishes a fresh create from, and
            // the Location header is the canonical path 033/044 will address with If-Match.
            // Published after `SaveChangesAsync` has returned, which for a relational provider means the transaction
            // is committed: notifying about a row that a constraint then rejects would have other clients re-read a
            // catalog that never contained it. §4.3's word is "committed", and this is the line that earns it. The
            // duplicate-key branch above returns early, so a refused write publishes nothing — asserted by
            // `A_rejected_write_publishes_nothing`.
            bus.Publish(entity.Id.ToString("D"));

            return Results.Created($"/api/applications/{entity.Id}", entity);
        });
        // BEHAVIOR-m3-backend-api-033. §4.3 spells PUT as a *full replacement* with If-Match required, and the
        // 428 it names for a missing precondition is mapped to 409 by that same table — which falls out for free
        // here, because IsCurrent refuses an absent header exactly as it refuses a stale one. No branch of its own
        // to write, and no branch that could rot untested.
        endpoints.MapPut("/api/applications/{id}", async (HttpContext http, string id, NewApplicationRequest request, JobTrackerDb db,
            ApplicationEventBus bus, CancellationToken ct, [FromHeader(Name = "If-Match")] string? ifMatch) =>
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

            var entity = await db.Applications.FirstOrDefaultAsync(
                a => a.Id == guid && a.OwnerId == SessionGate.RequireUserId(http), ct);
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
            bus.Publish(entity.Id.ToString("D"));
            return Results.Ok(entity);
        });

        // BEHAVIOR-m3-backend-api-035. Tracks the entity (no AsNoTracking) because it is being removed, and
        // returns the same 404 as the read path: "no such record" is one fact whether it surfaces on GET or DELETE.
        //
        // If-Match is ignored here, which is a known defect and not an oversight — 044's Red turns it into a
        // failing test first. See that commit's message for why a minimal 035 Green was written blind to it.
        endpoints.MapDelete("/api/applications/{id}", async (HttpContext http, string id, JobTrackerDb db, ApplicationEventBus bus, CancellationToken ct, [FromHeader(Name = "If-Match")] string? ifMatch) =>
        {
            if (!Guid.TryParse(id, out var guid))
            {
                return Problems.NotFound(id);
            }

            var entity = await db.Applications.FirstOrDefaultAsync(
                a => a.Id == guid && a.OwnerId == SessionGate.RequireUserId(http), ct);
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
            bus.Publish(guid.ToString("D"));
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
