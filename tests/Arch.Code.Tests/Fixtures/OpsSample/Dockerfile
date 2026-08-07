FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Api/Api.csproj -c Release -o /app

# A floating tag on the runtime image: the deployed runtime can change with no code change.
FROM mcr.microsoft.com/dotnet/aspnet:latest
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
EXPOSE 9090/udp
ENTRYPOINT ["dotnet", "Api.dll"]
