FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY FileManager/FileManager.csproj FileManager/
RUN dotnet restore FileManager/FileManager.csproj
COPY FileManager/ FileManager/
RUN dotnet publish FileManager/FileManager.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
COPY --from=build /app /app
RUN mkdir /data && chown app:app /data
WORKDIR /data
USER app
VOLUME /data
ENTRYPOINT ["dotnet", "/app/FileManager.dll"]
