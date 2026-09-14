using System.Diagnostics;
using System.Net;
using ContractService.Application.Abstractions;
using ContractService.Infrastructure;
using ContractService.Infrastructure.ProposalApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ContractService.UnitTests;

/// <summary>
/// Exercises the resilience pipeline exactly as composed by <see cref="DependencyInjection.AddContractInfrastructure"/>:
/// only the outermost socket handler is replaced, so retry, circuit breaker and per-attempt timeout are real.
/// </summary>
public sealed class ProposalResiliencePipelineTests
{
    [Fact]
    public async Task CR_01_ServerError_IsRetriedOnce_AndMapsToUnavailable()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var provider = BuildProvider(handler);

        var result = await Gateway(provider).GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Unavailable, result);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task CR_02_RetryCountZero_SendsASingleRequest()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var provider = BuildProvider(handler, settings => settings["ProposalApi:RetryCount"] = "0");

        var result = await Gateway(provider).GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Unavailable, result);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ProposalEligibilityResult.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, ProposalEligibilityResult.InvalidResponse)]
    public async Task CR_03_NonTransientResponses_AreNotRetried(HttpStatusCode statusCode, ProposalEligibilityResult expected)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        await using var provider = BuildProvider(handler);

        var result = await Gateway(provider).GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task CR_04_SlowDependency_HitsAttemptTimeout_RetriesOnce_AndStaysInsideTotalBudget()
    {
        var handler = new RecordingHandler(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var provider = BuildProvider(handler);

        var started = Stopwatch.GetTimestamp();
        var result = await Gateway(provider).GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal(ProposalEligibilityResult.Unavailable, result);
        Assert.Equal(2, handler.Requests);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"The total budget was exceeded: {elapsed}.");
    }

    [Fact]
    public async Task CR_05_CircuitBreaker_Opens_AndShortCircuitsWithoutReachingTheDependency()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(handler);
        var gateway = Gateway(provider);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(ProposalEligibilityResult.Unavailable, await gateway.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None));
        }

        var requestsBeforeOpenCircuit = handler.Requests;
        var afterBreak = await gateway.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Unavailable, afterBreak);
        Assert.Equal(requestsBeforeOpenCircuit, handler.Requests);
    }

    [Fact]
    public async Task CR_06_CallerCancellation_IsNotRetried_AndPropagates()
    {
        var handler = new RecordingHandler(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var provider = BuildProvider(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gateway(provider).GetEligibilityAsync(Guid.NewGuid(), cts.Token));
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("ProposalApi:BaseUrl", "not-an-url")]
    [InlineData("ProposalApi:TimeoutSeconds", "0")]
    [InlineData("ProposalApi:AttemptTimeoutSeconds", "5")]
    [InlineData("ProposalApi:RetryCount", "2")]
    public async Task CR_07_InvalidConfiguration_FailsFast(string key, string value)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var provider = BuildProvider(handler, settings => settings[key] = value);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ProposalApiOptions>>().Value);
    }

    private static IProposalGateway Gateway(ServiceProvider provider) => provider.GetRequiredService<IProposalGateway>();

    private static ServiceProvider BuildProvider(HttpMessageHandler handler, Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ContractDb"] = "Host=contract-db;Database=contracts;Username=postgres;Password=postgres",
            ["ProposalApi:BaseUrl"] = "http://proposal-api:8080",
            ["ProposalApi:TimeoutSeconds"] = "2",
            ["ProposalApi:AttemptTimeoutSeconds"] = "0.3",
            ["ProposalApi:RetryCount"] = "1"
        };
        configure?.Invoke(settings);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContractInfrastructure(configuration);
        services.AddHttpClient<IProposalGateway, ProposalHttpGateway>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _requests;

        public RecordingHandler(Func<CancellationToken, HttpResponseMessage> respond)
            : this(token => Task.FromResult(respond(token)))
        {
        }

        public int Requests => Volatile.Read(ref _requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            var response = await respond(cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
