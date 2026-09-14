using ContractService.Application.Abstractions;
using ContractService.Infrastructure.Persistence;
using ContractService.Infrastructure.ProposalApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Polly.Timeout;

namespace ContractService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddContractInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ContractDb");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("ConnectionStrings:ContractDb is required.");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database))
            throw new InvalidOperationException("ConnectionStrings:ContractDb must specify host and database.");
        services.AddOptions<ProposalApiOptions>().Bind(configuration.GetSection("ProposalApi"))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "ProposalApi:BaseUrl must be an absolute HTTP URL.")
            .Validate(options => double.IsFinite(options.TimeoutSeconds) && options.TimeoutSeconds >= 0.1 && options.TimeoutSeconds <= 60, "ProposalApi:TimeoutSeconds must be between 0.1 and 60.")
            .Validate(options => double.IsFinite(options.AttemptTimeoutSeconds) && options.AttemptTimeoutSeconds >= 0.05 && options.AttemptTimeoutSeconds <= options.TimeoutSeconds,
                "ProposalApi:AttemptTimeoutSeconds must be at least 0.05 and must not exceed ProposalApi:TimeoutSeconds.")
            .Validate(options => options.RetryCount is >= 0 and <= 1, "ProposalApi:RetryCount must be 0 or 1.")
            .ValidateOnStart();
        services.AddDbContext<ContractDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpClient<IProposalGateway, ProposalHttpGateway>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ProposalApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        }).AddResilienceHandler("proposal", (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<ProposalApiOptions>>().Value;

            // Outermost strategy first: retry wraps the circuit breaker, which wraps the per-attempt timeout.
            if (options.RetryCount > 0)
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.RetryCount,
                    Delay = TimeSpan.FromMilliseconds(50),
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(!args.Context.CancellationToken.IsCancellationRequested && IsTransient(args.Outcome))
                });
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = args => ValueTask.FromResult(!args.Context.CancellationToken.IsCancellationRequested && IsTransient(args.Outcome))
            });
            builder.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds)
            });
        });
        return services;
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException ||
        outcome.Result is { RequestMessage.Method.Method: "GET" } response && (int)response.StatusCode >= 500;

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
