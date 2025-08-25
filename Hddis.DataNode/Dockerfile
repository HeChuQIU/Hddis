FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Hddis.DataNode/Hddis.DataNode.csproj", "Hddis.DataNode/"]
RUN dotnet restore "Hddis.DataNode/Hddis.DataNode.csproj"
COPY . .
WORKDIR "/src/Hddis.DataNode"
RUN dotnet build "./Hddis.DataNode.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Hddis.DataNode.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Hddis.DataNode.dll"]
