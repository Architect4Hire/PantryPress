using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PantryPress.ApiService.Hosting;

namespace PantryPress.Tests.Api;

// PP1.12: a route that rotates the stamp hands the new one back only when the action succeeded. The filter runs before
// the result is written, so the response status is still 200 there; the result is what knows (an audit finding).
public sealed class SessionRefreshTests
{
    public static TheoryData<IActionResult?, bool> Results => new()
    {
        { new NoContentResult(), true },
        { new OkObjectResult(new object()), true },
        { new ObjectResult(new object()), true },
        { new ObjectResult(new ProblemDetails()) { StatusCode = StatusCodes.Status409Conflict }, false },
        { new ObjectResult(new ProblemDetails()) { StatusCode = StatusCodes.Status400BadRequest }, false },
        { new ChallengeResult(), false },
        { null, false },
    };

    [Theory]
    [MemberData(nameof(Results))]
    public void Only_a_successful_result_refreshes_the_session(IActionResult? result, bool refreshes) =>
        Assert.Equal(refreshes, RefreshesSessionAttribute.Succeeded(result));
}
