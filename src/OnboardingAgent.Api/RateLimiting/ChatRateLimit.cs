using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace OnboardingAgent.Api.RateLimiting;

public sealed class ChatRateLimitOptions
{
    public const string Section = "RateLimiting:Chat";

    /// <summary>Chat questions each user may ask per window. Protects the Bedrock budget and quotas.</summary>
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;

    public IEnumerable<string> Validate()
    {
        if (PermitLimit < 1)
        {
            yield return $"{Section}:PermitLimit ({PermitLimit}) must be at least 1.";
        }

        if (WindowSeconds < 1)
        {
            yield return $"{Section}:WindowSeconds ({WindowSeconds}) must be at least 1.";
        }
    }
}

/// <summary>Fixed-window limit on /api/chat, one window per authenticated user (or per IP without a user).</summary>
public static class ChatRateLimit
{
    public const string Policy = "chat";

    public static IServiceCollection AddChatRateLimit(this IServiceCollection services, ChatRateLimitOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(Policy, http => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                }));
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? (int)Math.Ceiling(wait.TotalSeconds)
                    : options.WindowSeconds;
                http.Response.Headers.RetryAfter = retryAfter.ToString();

                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ChatRateLimit))
                    .LogWarning("[RateLimit] {Path} rejected for {Partition}: limit {Limit} per {Window} s, retry after {RetryAfter} s",
                        http.Request.Path, PartitionKey(http), options.PermitLimit, options.WindowSeconds, retryAfter);

                await Results.Problem(
                        $"At most {options.PermitLimit} questions per {options.WindowSeconds} seconds. Try again in {retryAfter} seconds.",
                        title: "Too many questions.",
                        statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(http);
            };
        });
        return services;
    }

    private static string PartitionKey(HttpContext http)
    {
        var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.Identity?.Name;
        return user is null ? $"ip:{http.Connection.RemoteIpAddress}" : $"user:{user}";
    }
}
