FROM node:20-alpine AS ui-build
WORKDIR /ui

COPY ["src/BloodWatch.Api/ui/package.json", "./"]
RUN npm install

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

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BloodWatch.Api.dll"]
