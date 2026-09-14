FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy props and sln
COPY Directory.Build.props Directory.Packages.props global.json ./

# Copy csproj files for ProposalService
COPY src/ProposalService/ProposalService.Domain/ProposalService.Domain.csproj src/ProposalService/ProposalService.Domain/
COPY src/ProposalService/ProposalService.Application/ProposalService.Application.csproj src/ProposalService/ProposalService.Application/
COPY src/ProposalService/ProposalService.Infrastructure/ProposalService.Infrastructure.csproj src/ProposalService/ProposalService.Infrastructure/
COPY src/ProposalService/ProposalService.Api/ProposalService.Api.csproj src/ProposalService/ProposalService.Api/

# Restore dependencies
RUN dotnet restore src/ProposalService/ProposalService.Api/ProposalService.Api.csproj

# Copy source code and build
COPY src/ProposalService/ src/ProposalService/
RUN dotnet publish src/ProposalService/ProposalService.Api/ProposalService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ProposalService.Api.dll"]
