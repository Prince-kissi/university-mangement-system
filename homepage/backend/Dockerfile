FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ComplaintSystem.sln ./
COPY src/ComplaintSystem.Domain/ComplaintSystem.Domain.csproj src/ComplaintSystem.Domain/
COPY src/ComplaintSystem.Application/ComplaintSystem.Application.csproj src/ComplaintSystem.Application/
COPY src/ComplaintSystem.Infrastructure/ComplaintSystem.Infrastructure.csproj src/ComplaintSystem.Infrastructure/
COPY src/ComplaintSystem.Api/ComplaintSystem.Api.csproj src/ComplaintSystem.Api/
RUN dotnet restore src/ComplaintSystem.Api/ComplaintSystem.Api.csproj
COPY src/ src/
RUN dotnet publish src/ComplaintSystem.Api/ComplaintSystem.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl postgresql-client \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/storage/evidence /app/storage/backups /app/storage/keys \
    && chown -R app:app /app
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ComplaintSystem.Api.dll"]
