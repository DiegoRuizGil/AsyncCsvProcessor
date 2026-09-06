FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AsyncCsvProcessor.Domain/AsyncCsvProcessor.Domain.csproj AsyncCsvProcessor.Domain/
COPY src/AsyncCsvProcessor.Application/AsyncCsvProcessor.Application.csproj AsyncCsvProcessor.Application/
COPY src/AsyncCsvProcessor.Infrastructure/AsyncCsvProcessor.Infrastructure.csproj AsyncCsvProcessor.Infrastructure/
COPY src/AsyncCsvProcessor.Api/AsyncCsvProcessor.Api.csproj AsyncCsvProcessor.Api/

RUN dotnet restore AsyncCsvProcessor.Api/AsyncCsvProcessor.Api.csproj

COPY src/AsyncCsvProcessor.Domain/          AsyncCsvProcessor.Domain/
COPY src/AsyncCsvProcessor.Application/     AsyncCsvProcessor.Application/
COPY src/AsyncCsvProcessor.Infrastructure/  AsyncCsvProcessor.Infrastructure/
COPY src/AsyncCsvProcessor.Api/             AsyncCsvProcessor.Api/

RUN dotnet publish AsyncCsvProcessor.Api/AsyncCsvProcessor.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AsyncCsvProcessor.Api.dll"]