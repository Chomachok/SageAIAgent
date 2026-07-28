FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["backend/src/Sage.Core/Sage.Core.csproj", "Sage.Core/"]
COPY ["backend/src/Sage.Infrastructure/Sage.Infrastructure.csproj", "Sage.Infrastructure/"]
COPY ["backend/src/Sage.Api/Sage.Api.csproj", "Sage.Api/"]
RUN dotnet restore "Sage.Api/Sage.Api.csproj"

COPY backend/src/ .
WORKDIR "/src/Sage.Api"
RUN dotnet build "Sage.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sage.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sage.Api.dll"]