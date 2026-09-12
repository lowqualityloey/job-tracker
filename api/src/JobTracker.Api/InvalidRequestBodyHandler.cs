using Microsoft.AspNetCore.Diagnostics;

namespace JobTracker.Api;

/// <summary>
/// Turns a body the JSON binder refused into the problem document the contract promises. BEHAVIOR-069.
///
/// Registered as an <see cref="IExceptionHandler"/>, which <c>UseExceptionHandler()</c> consults before it falls back to
/// the generic 500. The handler is the right seam for one reason: it is handed the exception. Anything downstream of the
/// problem-details service sees only a document that has already been flattened to "an error occurred", which is precisely
/// the information the framework had already worked out and this app was discarding.
/// </summary>
internal sealed class InvalidRequestBodyHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Two conditions, both load-bearing. The type test keeps every other failure on the path it takes today -- an
        // actual bug must stay a 500 with a traceId, because answering 400 "your body was wrong" to a server-side defect
        // would move a real signal into the noise. The status test keeps the *other* BadHttpRequestException cases
        // (payload too large, an unreadable body, a media-type refusal) on their own codes rather than relabelling them
        // as validation the client can fix by editing a field.
        if (exception is not BadHttpRequestException badRequest
            || badRequest.StatusCode != StatusCodes.Status400BadRequest)
        {
            return false;
        }

        await Results
            .Problem(Problems.InvalidRequestBody(badRequest, httpContext.Request.Path))
            .ExecuteAsync(httpContext);

        return true;
    }
}
