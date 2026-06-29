FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY webserver-dotnet.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish webserver-dotnet.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "webserver-dotnet.dll"]
