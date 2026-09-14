using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProposalService.Application.Abstractions;
using ProposalService.Application.Proposals;
using ProposalService.Infrastructure.Persistence;

namespace ProposalService.Infrastructure;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

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
