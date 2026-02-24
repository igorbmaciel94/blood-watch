FROM node:20-alpine AS ui-build
WORKDIR /ui

COPY ["src/BloodWatch.Api/ui/package.json", "./"]
COPY ["src/BloodWatch.Api/ui/package-lock.json", "./"]
RUN npm ci --no-audit --no-fund

COPY ["src/BloodWatch.Api/ui/", "./"]
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["BloodWatch.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/BloodWatch.Core/BloodWatch.Core.csproj", "src/BloodWatch.Core/"]
COPY ["src/BloodWatch.Infrastructure/BloodWatch.Infrastructure.csproj", "src/BloodWatch.Infrastructure/"]
COPY ["src/BloodWatch.Adapters.Portugal/BloodWatch.Adapters.Portugal.csproj", "src/BloodWatch.Adapters.Portugal/"]
COPY ["src/BloodWatch.Api/BloodWatch.Api.csproj", "src/BloodWatch.Api/"]
RUN dotnet restore "src/BloodWatch.Api/BloodWatch.Api.csproj"

COPY . .
COPY --from=ui-build /wwwroot/app /src/src/BloodWatch.Api/wwwroot/app
WORKDIR "/src/src/BloodWatch.Api"
RUN dotnet publish "BloodWatch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER 10001:10001

ENTRYPOINT ["dotnet", "BloodWatch.Api.dll"]
