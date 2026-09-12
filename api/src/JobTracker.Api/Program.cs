using JobTracker.Api.Auth;
using JobTracker.Api;
using JobTracker.Api.Data;
using Npgsql;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure that no behaviour test can drive. RFC 9457 was wired in Slice 0 because it is a
// cross-cutting configuration of the pipeline rather than a behaviour; endpoints are not, so they arrive only
// with the failing test that requires them. The template's `MapGet("/", ...)` sample stays deleted: an endpoint
// with no test is untested behaviour in the tree.
builder.Services.AddProblemDetails();

// BEHAVIOR-069 / AC-8's family. A body the binder cannot read was already diagnosed by the framework as
// `InvalidJsonRequestBody` and wrapped in a `BadHttpRequestException` whose OWN StatusCode is 400 -- the correct answer is
// in this process two frames before the response is written, and the default mapping throws it away in favour of a generic
// 500 with no `code`. That combination is actively misleading: -060's client classifies an unrecognised document as
// `corrupt-data`, so the browser sees "the server sent something we cannot parse" while the log says "invalid JSON request
// body" -- two true statements pointing in opposite directions.
//
// Handled as an IExceptionHandler rather than by customising ProblemDetailsOptions, because the handler is handed the
// exception itself: it does not have to recover the cause from a document that has already been flattened to "500".
builder.Services.AddSingleton<IExceptionHandler, InvalidRequestBodyHandler>();

// BEHAVIOR-m3-backend-api-026: a catalog to be empty of. Registered here because the Red test above it fails
// with 404 without it, and not earlier because nothing needed a database until a test asked the API one.
// BEHAVIOR-047's service, registered now rather than at -047: the seam had no production caller until seeding needed
// one, and registering an unused service would have implied a wiring that no test yet required.
// BEHAVIOR-066 / spec 3.2: the clock, as a dependency rather than a static. Registered explicitly (not left to the host's
// own default) because who may read the time is now a question with a production answer and a test answer, and the place
// they differ should be visible in the composition root rather than in a test's last-write-wins override.
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddSingleton<IPasswordService, PasswordService>();

// BEHAVIOR-m4-auth-063: the antiforgery token is a *protected* session id, and the key ring is the framework's rather
// than a secret configured per environment — `Antiforgery`'s comment carries where that deviates from DECISION-m4-auth-006's
// "HMAC over the session id" wording, and why. Stated explicitly because this app never asked for data protection
// implicitly, and a service that arrives by accident is one nobody notices being removed.
builder.Services.AddDataProtection();

builder.Services.AddDbContext<JobTrackerDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// BEHAVIOR-m3-backend-api-046: the fan-out behind `GET /api/applications/events`. A singleton because its whole
// purpose is to connect a request that *writes* to requests that are *already open* — scoped or transient would give
// every connection a bus of one. It is also the visible form of ASSUMPTION-m3-backend-api-002 (single instance
// through M5): state lives in this process, so a second instance would notify only its own subscribers. Lifting that
// assumption means replacing this class with a Redis backplane or PostgreSQL `NOTIFY`, not rewriting the endpoints.
builder.Services.AddSingleton<ApplicationEventBus>();

// CORS: the origin list comes from configuration, and an absent list means no cross-origin access at all.
//
// The failure this fixes was invisible to every test that existed: `WebApplicationFactory` answers a request without
// asking permission first, and jsdom's `fetch` is a stub that enforces nothing, so 257 green tests coexisted with an
// API no browser could use (preflight 405, and no `Access-Control-Allow-Origin` on any response). `BEHAVIOR-…-042` is
// the behaviour that found it.
//
// **Strict on origins, open on headers and methods** — the direction is the decision. `If-Match` must survive
// preflight or optimistic writes fail only in browsers, AC-7's retry key will want another custom header, and a
// hand-maintained header allow-list fails on the *next* feature rather than the current one. Headers are a
// compatibility surface here, not a defence; the origin is the boundary a browser actually enforces.
//
// **`AllowCredentials` is now required, and it is a decision with a price.** M3 wrote the opposite sentence --
// "deliberately absent: M3 sends no cookies" -- and was right then. `-050` put a session cookie on the wire, so the pair
// `Access-Control-Allow-Origin: <exact>` + `Access-Control-Allow-Credentials: true` is what every cross-origin deployment
// of this API now depends on. The reason it is spelled out rather than trusted: the wildcard-and-credentials combination
// is refused by browsers with a network error that carries no HTTP status and no readable body, so a misconfiguration
// looks exactly like the server being down. `CorsCredentialsTests` pins both halves (the pair on preflight AND on the
// actual 401 response), pins that an unlisted origin gains nothing from this, and pins that a `*` written into
// `Cors:AllowedOrigins` can never produce the forbidden pair.
//
// **The one thing this cannot make loud from here:** whether a real browser accepts the pair and attaches the cookie.
// That is `-064`'s Chromium harness, which DECISION-m4-auth-007 (a′) unblocked — and note the interaction with `-071`
// directly below: once the harness page is served from THIS origin, it is same-origin, so **no preflight occurs at all**
// and this policy stops being exercised in the browser. That trade is accepted and recorded in the decision, not
// discovered later; `CorsCredentialsTests` is now the only place the pair is proven, and it stays a real host, not a mock.
// AC-16's operator error, made loud at boot instead of quiet at request time. Measured, not assumed: a literal "*" in
// this list goes to WithOrigins, which treats it as an origin string to match against a request's Origin header -- and no
// browser ever sends Origin: "*". The result is a policy that matches nothing: the preflight answers 204 with no
// Access-Control headers at all, every cross-origin client is refused, and nothing anywhere says why. (The pair AC-16 is
// written against -- wildcard origin plus credentials -- is not what happens, because the framework never reaches it.)
// A wildcard cannot carry credentials, so an operator who writes one has already lost the thing they were trying to fix.
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (configuredOrigins.Any(o => string.Equals(o.Trim(), "*", StringComparison.Ordinal)))
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins contains \"*\", which matches no real request and silently disables CORS for every " +
        "cross-origin client; and a wildcard origin can never be paired with Access-Control-Allow-Credentials (AC-16). " +
        "List the exact origins, for example \"http://127.0.0.1:5173\".");
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var allowed = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowed.Length > 0)
    {
        policy.WithOrigins(allowed).AllowAnyHeader().AllowAnyMethod()
        // BEHAVIOR-067 / AC-16: the pair a credentialed cross-origin request needs. Without it the browser strips the
        // __Host-JTSession cookie from any request that is not same-origin, and the user is told they are signed out.
        .AllowCredentials();
    }
}));

var app = builder.Build();

// Migrations apply at startup. Declared as a decision, not a default, because it has a real failure mode: two
// instances racing the same migration, or a slow migration holding the process open past a health check. Both
// are acceptable while there is one instance, no production data and nothing to be down — §4.2 keeps every M3
// change expand-only, so a migration can never take a lock that breaks a reader. Who applies migrations at
// *deploy* time is M5's subject (pk:ship), and the integration fixture depends on this line: it is how a
// Testcontainers database gets its schema without the test reaching into EF internals.
// BEHAVIOR-050: refuse to boot in Production with default credentials. BEFORE the migration on purpose — "refuse to
// start" should mean the process does no work on its way out, not that it applies a schema and then dies in a
// crash-loop. The alternative (a check after Migrate) would also leave a half-migrated database behind from an
// deployment that was never supposed to happen.
BootGuard.EnsureSafeToStart(app.Configuration, app.Environment);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobTrackerDb>();
    db.Database.Migrate();

    // BEHAVIOR-050: the bootstrap account is created from configuration, after the schema exists and before any request
    // can be served. Order matters: seeding before Migrate would fail on a fresh database, and seeding after app.Run()
    // would let the first request win the race against the account it needs to authenticate.
    await BootstrapUserSeed.SeedAsync(
        db,
        app.Configuration,
        scope.ServiceProvider.GetRequiredService<IPasswordService>(),
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BootstrapUserSeed)));
}

// RFC 9457 (spec DECISION-m3-backend-api-004): unhandled exceptions become application/problem+json, and
// empty 4xx/5xx responses get a problem body too, so the client adapter never parses a bare status code.
app.UseExceptionHandler();
app.UseStatusCodePages();

// The **global** policy, not per-endpoint `.RequireCors(...)`. `-046` is the argument: the SSE endpoint was added to
// the catalog long after the CRUD routes existed, and a per-endpoint policy is exactly the thing someone forgets on
// the endpoint they add next — which would strand the stream cross-origin while every write kept working.
app.UseCors();

// BEHAVIOR-055 / spec 2.1: the gate runs AFTER UseCors on purpose -- a preflight must not be refused for lacking a
// cookie it cannot carry -- and before the routes, so nothing can stream a single byte anonymously.
// `-071`'s static-file block is registered *below* this line rather than beside UseCors, so the gate still sees every
// request first and adding a file server cannot create a path that skips it. The reasoning lives with the block.
app.UseSessionGate();

// BEHAVIOR-m4-auth-063 / AC-11. Registered strictly **after** the gate above, and the order carries meaning twice over.
// A request with no session is a `401` — an authentication failure the client maps to "sign in"; a request *with* one but
// without a matching token is a `403`. Run in the other order and every anonymous probe becomes a `403` describing a
// token the caller never had a chance to hold, which would also move `DataRouteAuthTests`' ratified 401s under a row that
// has no business touching them. Both directions are asserted: `…AntiforgeryGateTests` for the 403, and that existing
// file for the 401s it must not disturb.
app.UseAntiforgeryGate();

// Routes and handlers live in ApplicationCatalog (spec §4.1's deep module); Program is composition.
app.MapAuthCatalog();
app.MapApplicationCatalog();

// BEHAVIOR-071 / DECISION-m4-auth-007 option (a′), as amended by measurement — **the API serves the SPA as its own
// origin, in Development only.**
//
// Why: Chromium's *site* is scheme + host and **ignores the port**, so a harness page on one port talking to an API on
// another is two sites, `SameSite=Lax` refuses to attach the session cookie, and AC-12 fails while nothing in the
// product is broken. Serving the page from the API's origin removes that whole class of false failure. The measurement
// recorded at the decision's foot adds the other half: over plain `http` Chromium will not **store** a `__Host-` cookie
// at *any* address, loopback included — so same-origin is necessary and not sufficient, and the origin has to be TLS.
// TLS needs no code here: Kestrel reads a certificate from `Kestrel__Certificates__Default__Path` + `__KeyPath`.
//
// Why Development only: `docs/aws-deployment.md` puts the front end and the API on **different origins** when deployed,
// which is exactly what makes AC-16's credentialed CORS load-bearing. Serving a bundle from the API in production would
// place a second copy of the app on the API's own origin, where its requests bypass the boundary everyone believes in.
if (app.Environment.IsDevelopment())
{
    // Relative to the content root, so `appsettings.Development.json` can name `../../dist` — the Vite output, two
    // levels up from `api/src/JobTracker.Api` — without this file learning where the repository lives.
    var configured = app.Configuration["Web:SpaRoot"];
    var spaRoot = string.IsNullOrWhiteSpace(configured)
        ? null
        : Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(app.Environment.ContentRootPath, configured));

    if (spaRoot is null)
    {
        // Unset is the normal state on a machine that has never built the front end, and in CI's api job. Logged rather
        // than left silent because the person who *means* to serve a bundle would otherwise read a wall of 404s and
        // look for the bug in the API.
        app.Logger.LogInformation(
            "Web:SpaRoot is not set, so this process serves the API only. Build the front end and point Web:SpaRoot at " +
            "the output directory to serve it from this origin as well.");
    }
    else if (!Directory.Exists(spaRoot))
    {
        app.Logger.LogWarning(
            "Web:SpaRoot is configured as {SpaRoot} and no such directory exists, so this process serves the API only.",
            spaRoot);
    }
    else
    {
        // An explicit provider, not `webroot`: `wwwroot` would mean copying the build output into the API project, and
        // the bundle is a frontend artefact that happens to be published by this process.
        var bundle = new PhysicalFileProvider(spaRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = bundle });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = bundle, RequestPath = "" });

        var shell = Path.Combine(spaRoot, "index.html");

        // Two shapes were tried before this one, and each was rejected by an observed failure rather than a preference:
        //
        //   · **Middleware that awaits the pipeline and looks for a 404.** It does not work in *this* pipeline:
        //     `app.UseStatusCodePages()` is registered above it and defers the status write, so the 404 was not
        //     observable where the check asked for it. Eight of this row's cases still passed — only the deep-route case
        //     reads that status — which is the whole lesson: a nearly-green file hid a dead branch. The mechanism is
        //     stated only as far as it is proven, and the fix removes the dependency on the read rather than trusting an
        //     explanation that had not been pinned down.
        //   · **`MapFallback`.** It passed `-071` and **broke a ratified row**: `ApplicationsCommandTests.Non_json_body_rejected`
        //     went `415 → 404`, in isolation, with the SPA block as the only difference. A *fallback* is consulted for
        //     requests the API ought to be deciding for itself, so the `/api` clause below converted the framework's 415
        //     into a 404 — quietly destroying `-045`'s guard, which makes Content-Type the thing that refuses a simple
        //     cross-origin write. Only the full suite saw it; this row's own cases were all green.
        //
        // A catch-all `MapGet` ranks below a literal route, so the API's endpoint wins selection and keeps its status —
        // and the verb discipline arrives free: a POST to a page path matches nothing, and routing answers 405.
        //
        // Two guards remain, and neither is decoration:
        //   · `nonfile` — a path containing a dot is a named file, and a missing one must stay a `404`. Answering
        //     `/assets/typo.js` with the shell produces a `200` full of HTML handed to a script tag, which a browser
        //     reports as a syntax error with no hint that the file was never there;
        //   · the `/api` clause below — `nonfile` happily matches `/api/anything-without-a-dot`, so without this a
        //     typo'd or removed endpoint becomes `200 text/html` carrying the app shell. For a JSON client that is the
        //     worst shape available: the status says success, the body is not the contract, and the failure surfaces as
        //     a parser error far from its cause.
        app.MapGet("/{*path:nonfile}", async ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                // Not the shell's request. Empty body, and `UseStatusCodePages` above gives it the same RFC 9457
                // problem document every other 404 in this API carries.
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!File.Exists(shell))
            {
                // Rebuilt away from under a running process, most likely. Fall through to the API's own 404 problem
                // document rather than throwing from a catch-all, which would turn a missing file into a 500 on every
                // route in the application.
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.SendFileAsync(shell);
        });
    }
}

app.Run();



/// <summary>
/// Exposes the entry point to <c>WebApplicationFactory&lt;Program&gt;</c>. Minimal APIs compile
/// <c>Program</c> as an internal class, so without this partial declaration the integration tests could not
/// host the app in-process and would have to shell out to a live server.
/// </summary>
public partial class Program;
