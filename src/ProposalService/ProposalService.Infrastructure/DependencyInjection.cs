using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProposalService.Application.Abstractions;
using ProposalService.Application.Proposals;
using ProposalService.Infrastructure.Persistence;

namespace ProposalService.Infrastructure;

public sealed class SystemClock : IClock
{
    // PostgreSQL timestamptz keeps microseconds. Truncating at the source makes the timestamp returned by a
    // write identical to the one read back afterwards, instead of differing in the last tick.
    public DateTimeOffset UtcNow
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddProposalInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<ProposalDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProposalRepository, ProposalRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICreateProposalUseCase, CreateProposalUseCase>();
        services.AddScoped<IGetProposalByIdUseCase, GetProposalByIdUseCase>();
        services.AddScoped<IListProposalsUseCase, ListProposalsUseCase>();
        services.AddScoped<IChangeProposalStatusUseCase, ChangeProposalStatusUseCase>();
        services.AddHealthChecks().AddDbContextCheck<ProposalDbContext>("database", tags: ["ready"]);
        return services;
    }
}
