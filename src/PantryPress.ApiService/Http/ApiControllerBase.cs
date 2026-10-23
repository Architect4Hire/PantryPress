using Microsoft.AspNetCore.Mvc;
using PantryPress.Domain.Managers.Results;

namespace PantryPress.ApiService.Http;

/// <summary>
/// The base for API controllers: thin (ARCH-002), mapping a facade's <see cref="Result"/> to HTTP and nothing else.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult NoContentOr(Result result) => result.IsSuccess ? NoContent() : Failure(result.Error);

    protected IActionResult AcceptedOr(Result result) => result.IsSuccess ? Accepted() : Failure(result.Error);

    protected IActionResult OkOr<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : Failure(result.Error);

    protected ObjectResult Failure(Error error) => ApiProblems.FromError(this, error);
}
