FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/OficinaMecanica.Execucao.Domain/*.csproj src/OficinaMecanica.Execucao.Domain/
COPY src/OficinaMecanica.Execucao.Application/*.csproj src/OficinaMecanica.Execucao.Application/
COPY src/OficinaMecanica.Execucao.Infrastructure/*.csproj src/OficinaMecanica.Execucao.Infrastructure/
COPY src/OficinaMecanica.Execucao.API/*.csproj src/OficinaMecanica.Execucao.API/
RUN dotnet restore src/OficinaMecanica.Execucao.API/OficinaMecanica.Execucao.API.csproj
COPY src/ src/
RUN dotnet publish src/OficinaMecanica.Execucao.API/OficinaMecanica.Execucao.API.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "OficinaMecanica.Execucao.API.dll"]
