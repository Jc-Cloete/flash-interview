FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FlashInterview.slnx ./
COPY src/FlashInterview.Application/FlashInterview.Application.csproj src/FlashInterview.Application/
COPY src/FlashInterview.Hosting/FlashInterview.Hosting.csproj src/FlashInterview.Hosting/
COPY src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj src/FlashInterview.Infrastructure/
COPY src/FlashInterview.Api/FlashInterview.Api.csproj src/FlashInterview.Api/
RUN dotnet restore src/FlashInterview.Api/FlashInterview.Api.csproj

COPY . .
RUN dotnet publish src/FlashInterview.Api/FlashInterview.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY docs/sql_sensitive_list.txt docs/sql_sensitive_list.txt
EXPOSE 8080
ENTRYPOINT ["dotnet", "FlashInterview.Api.dll"]
