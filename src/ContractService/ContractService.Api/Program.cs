using System.Diagnostics;
using ContractService.Api.ErrorHandling;
using ContractService.Api.OpenApi;
using ContractService.Api.Serialization;
using ContractService.Application.Contracts;
using ContractService.Infrastructure;
using ContractService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddContractInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICreateContractUseCase, CreateContract>();
builder.Services.AddScoped<IGetContractByIdUseCase, GetContractById>();
builder.Services.AddScoped<IGetContractByProposalUseCase, GetContractByProposal>();
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new UtcTimestampConverter())).ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var response = ApiProblems.From(ContractError.ValidationFailed, context.HttpContext);
        if (response.Value is Microsoft.AspNetCore.Mvc.ProblemDetails problem)
            problem.Extensions["errors"] = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(x => x.Key, _ => new[] { "The field has an invalid or missing value." });
        return response;
    });
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
    context.ProblemDetails.Extensions.TryAdd("code", ApiProblems.CodeForStatus(status));
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

    // Only server failures are masked; 4xx keep the reason the pipeline produced.
    if (status >= 500) context.ProblemDetails.Detail = "An unexpected error occurred.";
});
builder.Services.AddHealthChecks().AddDbContextCheck<ContractDbContext>("database", tags: ["ready"]);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Contract Service",
        Version = "v1",
        Description = "Approved proposal contracting. Authentication is outside the scope of this technical exercise."
    });
    options.SchemaFilter<ContractSchemaFilter>();
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["Service"] = "ContractService",
        ["Environment"] = app.Environment.EnvironmentName,
        ["TraceId"] = Activity.Current?.Id ?? context.TraceIdentifier
    });
    await next(context);
});
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ContractDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
}
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

await app.RunAsync();

public partial class Program
{
}
