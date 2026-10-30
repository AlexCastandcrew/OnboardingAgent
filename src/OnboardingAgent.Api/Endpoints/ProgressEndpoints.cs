using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OnboardingAgent.Core.Catalog;
using OnboardingAgent.Core.Contracts;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Progress;

namespace OnboardingAgent.Api.Endpoints;

/// <summary>Who may open the admin view: a token with the role claim "admin", or a user listed in <c>Progress:AdminUsers</c> (local demos).</summary>
public static class AdminPolicy
{
    public const string Name = "Admin";

    public static bool IsAdmin(ClaimsPrincipal user, ProgressOptions options) =>
        user.IsInRole("admin")
        || (ProgressEndpoints.UserId(user) is { } id && options.AdminUsers.Contains(id, StringComparer.OrdinalIgnoreCase));
}

/// <summary>Personal progress of the signed-in user and the aggregated view for admins.</summary>
public static class ProgressEndpoints
{
    private const int MaxSteps = 40;
    private const int MaxStepKeyChars = 80;

    public static void MapProgress(this RouteGroupBuilder api)
    {
        api.MapGet("/me", Me)
            .WithTags("Progress")
            .WithSummary("The signed-in user and whether they are an admin.");

        api.MapGet("/me/progress", MyProgressOf)
            .WithTags("Progress")
            .WithSummary("Your own onboarding progress: level, steps, day, questions asked (a count) and pace.");

        api.MapPut("/me/progress", Save)
            .WithTags("Progress")
            .WithSummary("Stores which steps of the route you marked as done.")
            .ProducesValidationProblem();

        api.MapGet("/admin/overview", Overview)
            .RequireAuthorization(AdminPolicy.Name)
            .WithTags("Admin")
            .WithSummary("Level, steps, day and pace of every person in onboarding. Admins only. Never includes question texts.")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    internal static string? UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name;

    private static string Label(ClaimsPrincipal user) => user.Identity?.Name ?? UserId(user) ?? "unknown";

    /// <summary>Counts a question for the user. Called by the chat endpoints after the request validated.</summary>
    internal static void RecordQuestion(ClaimsPrincipal user, ProgressStore store)
    {
        if (UserId(user) is { } id)
        {
            store.RecordQuestion(id, Label(user));
        }
    }

    private static MeResponse Me(ClaimsPrincipal user, ProgressOptions options) => new(Label(user), AdminPolicy.IsAdmin(user, options));

    private static MyProgress MyProgressOf(ClaimsPrincipal user, ProgressStore store, ProgressOptions options, TimeProvider clock) =>
        ToMine(UserId(user) is { } id ? store.Get(id) : null, Label(user), options, clock.GetUtcNow());

    private static Results<Ok<MyProgress>, ValidationProblem> Save(
        SaveProgressRequest request, ClaimsPrincipal user, ProjectCatalog catalog, ProgressStore store, ProgressOptions options, TimeProvider clock)
    {
        var role = request.Role?.ToLowerInvariant();
        var scope = catalog.Validate(request.ProjectId, role, request.Domain, request.Initiative);
        var errors = new Dictionary<string, string[]>(scope.Errors);

        var steps = request.DoneSteps ?? [];
        if (steps.Count > MaxSteps || steps.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > MaxStepKeyChars))
        {
            errors["doneSteps"] = [$"At most {MaxSteps} step keys, each between 1 and {MaxStepKeyChars} characters."];
        }

        if (request.TotalSteps is < 0 or > MaxSteps)
        {
            errors["totalSteps"] = [$"Total steps must be between 0 and {MaxSteps}."];
        }

        if (UserId(user) is not { } id)
        {
            errors["user"] = ["The token has no user."];
        }
        else if (errors.Count == 0)
        {
            var saved = store.Save(id, Label(user), new ProgressUpdate(scope.Project!.ProjectId, role!, scope.Domain, scope.Initiative, steps, request.TotalSteps));
            return TypedResults.Ok(ToMine(saved, Label(user), options, clock.GetUtcNow()));
        }

        return TypedResults.ValidationProblem(errors);
    }

    private static AdminOverview Overview(ProgressStore store, ProgressOptions options, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var people = store.All().Select(p =>
        {
            var mine = ToMine(p, p.Label, options, now);
            return new PersonProgress(p.Label, p.Domain ?? "general", p.Role ?? "unknown", mine.Level, mine.StepsDone, mine.StepsTotal, mine.Day, mine.Pace, p.LastActivityAt, p.Sample);
        }).ToList();
        return new AdminOverview(people, options.TargetDays);
    }

    private static MyProgress ToMine(UserProgress? p, string label, ProgressOptions options, DateTimeOffset now)
    {
        if (p is null)
        {
            return new MyProgress(label, 1, 0, 0, 1, 0, ProgressMath.OnPace, options.TargetDays, null);
        }

        var done = p.TotalSteps > 0 ? Math.Min(p.DoneSteps.Count, p.TotalSteps) : p.DoneSteps.Count;
        var day = ProgressMath.Day(p.StartedAt, now);
        return new MyProgress(
            label,
            ProgressMath.Level(done, p.TotalSteps),
            done,
            p.TotalSteps,
            day,
            p.QuestionsAsked,
            ProgressMath.Pace(day, done, p.TotalSteps, options.TargetDays),
            options.TargetDays,
            p.LastActivityAt);
    }
}
