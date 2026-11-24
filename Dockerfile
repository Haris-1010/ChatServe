# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ChatServe.Api/ChatServe.Api.csproj src/ChatServe.Api/
RUN dotnet restore src/ChatServe.Api/ChatServe.Api.csproj
COPY src/ src/
RUN dotnet publish src/ChatServe.Api/ChatServe.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ChatServe.Api.dll"]
