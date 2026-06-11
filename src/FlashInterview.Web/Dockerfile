FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FlashInterview.slnx ./
COPY src/FlashInterview.Application/FlashInterview.Application.csproj src/FlashInterview.Application/
COPY src/FlashInterview.Hosting/FlashInterview.Hosting.csproj src/FlashInterview.Hosting/
COPY src/FlashInterview.Web/FlashInterview.Web.csproj src/FlashInterview.Web/
RUN dotnet restore src/FlashInterview.Web/FlashInterview.Web.csproj

COPY . .
RUN dotnet publish src/FlashInterview.Web/FlashInterview.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FlashInterview.Web.dll"]
