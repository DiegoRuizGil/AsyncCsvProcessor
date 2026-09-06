FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AsyncCsvProcessor.Domain/AsyncCsvProcessor.Domain.csproj AsyncCsvProcessor.Domain/
COPY src/AsyncCsvProcessor.Application/AsyncCsvProcessor.Application.csproj AsyncCsvProcessor.Application/
COPY src/AsyncCsvProcessor.Infrastructure/AsyncCsvProcessor.Infrastructure.csproj AsyncCsvProcessor.Infrastructure/
COPY src/AsyncCsvProcessor.Worker/AsyncCsvProcessor.Worker.csproj AsyncCsvProcessor.Worker/

RUN dotnet restore AsyncCsvProcessor.Worker/AsyncCsvProcessor.Worker.csproj

COPY src/AsyncCsvProcessor.Domain/          AsyncCsvProcessor.Domain/
COPY src/AsyncCsvProcessor.Application/     AsyncCsvProcessor.Application/
COPY src/AsyncCsvProcessor.Infrastructure/  AsyncCsvProcessor.Infrastructure/
COPY src/AsyncCsvProcessor.Worker/          AsyncCsvProcessor.Worker/

RUN dotnet publish AsyncCsvProcessor.Worker/AsyncCsvProcessor.Worker.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AsyncCsvProcessor.Worker.dll"]