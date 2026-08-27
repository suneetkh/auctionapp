FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY App.Api/App.Api.csproj App.Api/
RUN dotnet restore App.Api/App.Api.csproj
COPY App.Api/ App.Api/
RUN dotnet publish App.Api/App.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "App.Api.dll"]
