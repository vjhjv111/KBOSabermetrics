FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish server/src/NaverSabermetrics.Web/NaverSabermetrics.Web.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .

# Render normally sets PORT=10000. The shell form below expands the runtime value.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 10000
USER $APP_UID
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet NaverSabermetrics.Web.dll"]
