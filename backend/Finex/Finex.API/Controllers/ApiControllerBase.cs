using Finex.API.Contracts.Common;
using Finex.Application.Common.Results;
using Finex.API.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Finex.API.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse> OkResponse(string message = "Operation completed successfully.")
        => Ok(ApiResponse.Ok(message));

    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data, string message = "Operation completed successfully.")
        => Ok(ApiResponse<T>.Ok(data, message));

    protected ActionResult<ApiResponse> FromResult(Result result, string successMessage = "Operation completed successfully.")
        => result.ToActionResult(this, successMessage);

    protected ActionResult<ApiResponse<T>> FromResult<T>(Result<T> result, string successMessage = "Operation completed successfully.")
        => result.ToActionResult(this, successMessage);
}