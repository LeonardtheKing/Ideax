using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Ideax.DTOs;

namespace Ideax.Filters;

public class ApiResponseWrapperFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // let MVC execute the result first
        var result = context.Result;

        // If the result is already a formatted ApiResponse, do nothing
        object? value = null;
        int? statusCode = null;

        switch (result)
        {
            case ObjectResult or OkObjectResult or BadRequestObjectResult or NotFoundObjectResult or CreatedAtActionResult:
                var obj = (ObjectResult)result;
                value = obj.Value;
                statusCode = obj.StatusCode;
                break;
            case JsonResult jr:
                value = jr.Value;
                break;
            case ContentResult cr:
                value = cr.Content;
                break;
            case EmptyResult:
                value = null;
                break;
            default:
                // leave other result types (FileResult, RedirectResult, StatusCodeResult) untouched
                await next();
                return;
        }

        if (value is ApiResponse || (value != null && value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>)))
        {
            await next();
            return;
        }

        // Wrap into ApiResponse according to status code
        if (statusCode == null || (statusCode >= 200 && statusCode < 300))
        {
            var wrapped = ApiResponse.Ok(value);
            context.Result = new ObjectResult(wrapped) { StatusCode = statusCode ?? 200 };
        }
        else
        {
            // Attempt to extract error messages
            IEnumerable<string>? errors = null;
            string? message = null;
            if (value is string s) message = s;
            else if (value is IEnumerable<string> seq) errors = seq;
            else if (value != null)
            {
                // if it's an anonymous/object result with fields, try to read a 'message' or 'errors' property via reflection
                var t = value.GetType();
                var prop = t.GetProperty("Message") ?? t.GetProperty("message");
                if (prop != null) message = prop.GetValue(value)?.ToString();
                var errsProp = t.GetProperty("Errors") ?? t.GetProperty("errors");
                if (errsProp != null && typeof(IEnumerable<string>).IsAssignableFrom(errsProp.PropertyType))
                {
                    errors = (IEnumerable<string>?)errsProp.GetValue(value);
                }
            }

            var wrapped = ApiResponse.Fail(message, errors);
            context.Result = new ObjectResult(wrapped) { StatusCode = statusCode };
        }

        await next();
    }
}
