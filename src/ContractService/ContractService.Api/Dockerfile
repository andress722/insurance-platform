FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy props and sln
COPY Directory.Build.props Directory.Packages.props global.json ./

# Copy csproj files for ContractService
COPY src/ContractService/ContractService.Domain/ContractService.Domain.csproj src/ContractService/ContractService.Domain/
COPY src/ContractService/ContractService.Application/ContractService.Application.csproj src/ContractService/ContractService.Application/
COPY src/ContractService/ContractService.Infrastructure/ContractService.Infrastructure.csproj src/ContractService/ContractService.Infrastructure/
COPY src/ContractService/ContractService.Api/ContractService.Api.csproj src/ContractService/ContractService.Api/

# Restore dependencies
RUN dotnet restore src/ContractService/ContractService.Api/ContractService.Api.csproj

# Copy source code and build
COPY src/ContractService/ src/ContractService/
RUN dotnet publish src/ContractService/ContractService.Api/ContractService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ContractService.Api.dll"]
