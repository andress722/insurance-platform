using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ProposalService.Api.ErrorHandling;
using ProposalService.Api.OpenApi;
using ProposalService.Application.Proposals;
using ProposalService.Infrastructure;
using ProposalService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
var connection = builder.Configuration.GetConnectionString("ProposalDb");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:ProposalDb is required.");
builder.Services.AddProposalInfrastructure(connection);
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
    ApiErrors.Create(context.HttpContext, new Error("validation_failed", "One or more fields are invalid.", context.ModelState.Keys.FirstOrDefault() ?? "request")));
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
    context.ProblemDetails.Extensions.TryAdd("code", ApiErrors.CodeForStatus(status));
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

    // Only server failures are masked; 4xx keep the reason the pipeline produced.
    if (status >= 500) context.ProblemDetails.Detail = "An unexpected error occurred.";
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ProposalService", Version = "v1", Description = "Proposal lifecycle API. Authentication is outside the scope of this technical assessment." });
    options.SchemaFilter<ProposalSchemaFilter>();
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["Service"] = "ProposalService",
        ["Environment"] = app.Environment.EnvironmentName,
        ["TraceId"] = Activity.Current?.Id ?? context.TraceIdentifier
    });
    await next(context);
});
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ProposalDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
}
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
await app.RunAsync();
public partial class Program { }
