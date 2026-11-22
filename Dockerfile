FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Mangrove.Cms/Mangrove.Cms.csproj src/Mangrove.Cms/
RUN dotnet restore src/Mangrove.Cms/Mangrove.Cms.csproj
COPY . .
RUN dotnet publish src/Mangrove.Cms/Mangrove.Cms.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    Mangrove__DataDirectory=/data
VOLUME ["/data", "/app/wwwroot/uploads"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mangrove.Cms.dll"]
