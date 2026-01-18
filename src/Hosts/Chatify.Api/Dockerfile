FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Hosts/Chatify.Api/Chatify.Api.csproj"
RUN dotnet publish "src/Hosts/Chatify.Api/Chatify.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
EXPOSE 8443
ENTRYPOINT ["dotnet", "Chatify.Api.dll"]
