using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class QuestionLibraryEndpoints
{
    public static void MapQuestionLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        // Tag list + availability are harmless (no answers) — access-gated so event admins see them too.
        app.MapGet("/api/question-library/tags", async (QuestionLibraryService lib, CancellationToken ct) =>
            Results.Ok(await lib.TagsAsync(ct)));

        app.MapGet("/api/question-library/available", async (string? tags, QuestionLibraryService lib, CancellationToken ct) =>
        {
            var list = (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(await lib.AvailableCountAsync(list, ct));
        });

        // Pull random questions into a Draft quiz/tipspromenad and mark them used (host / event admin).
        app.MapPost("/api/activities/{id:int}/questions/from-library", async (
            int id, LibraryGenerateRequest req, QuestionLibraryService lib, CancellationToken ct) =>
        {
            var (added, available) = await lib.GenerateIntoAsync(id, req.Count, req.Tags ?? new List<string>(), ct);
            return Results.Ok(new LibraryGenerateResult { Added = added, Available = available });
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Forget which questions have been used, so the whole library can be drawn again (host).
        app.MapPost("/api/question-library/reset-usage", async (QuestionLibraryService lib, CancellationToken ct) =>
        {
            await lib.ResetUsageAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter<AdminEndpointFilter>();
    }
}
