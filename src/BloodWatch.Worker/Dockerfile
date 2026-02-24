FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["BloodWatch.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/BloodWatch.Core/BloodWatch.Core.csproj", "src/BloodWatch.Core/"]
COPY ["src/BloodWatch.Infrastructure/BloodWatch.Infrastructure.csproj", "src/BloodWatch.Infrastructure/"]
COPY ["src/BloodWatch.Adapters.Portugal/BloodWatch.Adapters.Portugal.csproj", "src/BloodWatch.Adapters.Portugal/"]
COPY ["src/BloodWatch.Worker/BloodWatch.Worker.csproj", "src/BloodWatch.Worker/"]
RUN dotnet restore "src/BloodWatch.Worker/BloodWatch.Worker.csproj"

COPY . .
WORKDIR "/src/src/BloodWatch.Worker"
RUN dotnet publish "BloodWatch.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
USER 10001:10001

ENTRYPOINT ["dotnet", "BloodWatch.Worker.dll"]
