using Finex.API.Contracts.Common;
using Finex.Application.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace Finex.API.Extensions;

public static class ResultExtensions
{
    public static ActionResult<ApiResponse> ToActionResult(
        this Result result,
        ControllerBase controller,
        string successMessage = "Operation completed successfully.")
    {
        if (result.IsSuccess)
            return controller.Ok(ApiResponse.Ok(successMessage));

        return result.ToProblemResult(controller);
    }

    public static ActionResult<ApiResponse<T>> ToActionResult<T>(
        this Result<T> result,
        ControllerBase controller,
        string successMessage = "Operation completed successfully.")
    {
        if (result.IsSuccess)
            return controller.Ok(ApiResponse<T>.Ok(result.Value, successMessage));

        return result.ToProblemResult(controller);
    }

    private static ObjectResult ToProblemResult(this Result result, ControllerBase controller)
    {
        int statusCode = (int)result.Error.Type;

        return controller.Problem(
            statusCode: statusCode,
            title: result.Error.Code,
            detail: result.Error.Description);
    }
}