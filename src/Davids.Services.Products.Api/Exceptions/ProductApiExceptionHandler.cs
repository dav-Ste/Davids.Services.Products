using Davids.Services.Products.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Davids.Services.Products.Api.Exceptions;

public sealed class ProductApiExceptionHandler(ILogger<ProductApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = domain switch
        {
            ProductNotFoundException => StatusCodes.Status404NotFound,
            DuplicateProductSkuException => StatusCodes.Status409Conflict,
            InsufficientProductStockException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        logger.LogWarning("Products domain error {Code}: {Message}", domain.Code, domain.Message);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            code = domain.Code,
            reason = domain.Message
        }, cancellationToken);
        return true;
    }
}
