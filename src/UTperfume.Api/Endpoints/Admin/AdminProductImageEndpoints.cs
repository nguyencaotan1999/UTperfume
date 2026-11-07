using Microsoft.AspNetCore.Mvc;
using UTperfume.Api.Infrastructure;
using UTperfume.Application.Features.Admin.Images;

namespace UTperfume.Api.Endpoints.Admin;

/// <summary>
/// Product image upload and delete (P4-06). The admin group supplies authorization and the antiforgery check, which runs
/// before the multipart form is read (AdminAntiforgeryMiddleware). P4-15 adds the metadata <c>PUT</c> to this file.
/// </summary>
internal static class AdminProductImageEndpoints
{
    /// <summary>
    /// The request body limit: the 5 MB file (BR-A-24) plus room for the multipart framing and the text fields. A file
    /// over 5 MB but inside this limit is answered by the service as 413 <c>image_too_large</c>; a larger body is
    /// refused by the server with 413 before it is buffered.
    /// </summary>
    public const long MaxRequestBodyBytes = ImageUploadService.MaxFileBytes + 64 * 1024;

    public static void MapAdminProductImageEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapPost("/products/{id:int}/images", UploadAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithName("UploadProductImage")
            .WithTags("Admin product images")
            .Produces<AdminImage>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        admin.MapDelete("/products/{id:int}/images/{imageId:int}", DeleteAsync)
            .WithName("DeleteProductImage")
            .WithTags("Admin product images")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UploadAsync(
        int id,
        HttpRequest request,
        IFormFile? file,
        [FromForm] string? altText,
        [FromForm] string? isPrimary,
        ProductImageCommands commands,
        CancellationToken cancellationToken)
    {
        // Errors are keyed by parameter (architecture 6.2). Both checks run before anything is read or stored.
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Form.Files.Count > 1)
        {
            errors[ImageUploadService.FileField] = ["Exactly one file is allowed per request."];
        }

        bool? primary = null;
        if (!string.IsNullOrWhiteSpace(isPrimary))
        {
            if (bool.TryParse(isPrimary, out var parsed))
            {
                primary = parsed;
            }
            else
            {
                errors["isPrimary"] = ["isPrimary must be true or false."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using var content = file?.OpenReadStream();
        var upload = new ProductImageUpload(
            file is null || content is null ? null : new ImageUploadFile(content, file.Length, file.ContentType, file.FileName),
            altText,
            primary);

        var result = await commands.UploadAsync(id, upload, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"{AdminEndpoints.RoutePrefix}/products/{id}/images/{result.Value!.Id}", result.Value)
            : result.Error!.ToProblem();
    }

    private static async Task<IResult> DeleteAsync(
        int id, int imageId, ProductImageCommands commands, CancellationToken cancellationToken)
    {
        var result = await commands.DeleteAsync(id, imageId, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }
}
