#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Cyclops.MultiCluster/Cyclops.MultiCluster.csproj", "src/Cyclops.MultiCluster/"]
COPY ["src/Cyclops.MultiCluster.Tests/Cyclops.MultiCluster.Tests.csproj", "src/Cyclops.MultiCluster.Tests/"]
RUN dotnet restore "src/Cyclops.MultiCluster/Cyclops.MultiCluster.csproj"
RUN dotnet restore "src/Cyclops.MultiCluster.Tests/Cyclops.MultiCluster.Tests.csproj"
COPY . .
RUN dotnet build "src/Cyclops.MultiCluster/Cyclops.MultiCluster.csproj" -c Release
RUN dotnet build "src/Cyclops.MultiCluster.Tests/Cyclops.MultiCluster.Tests.csproj" -c Release
RUN dotnet test "src/Cyclops.MultiCluster.Tests/Cyclops.MultiCluster.Tests.csproj" -c Release --no-build

FROM build AS publish
RUN dotnet publish "src/Cyclops.MultiCluster/Cyclops.MultiCluster.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
ARG DEBUG=0
ENV DEBUG=${DEBUG}
SHELL [ "/bin/bash", "-c" ]
RUN [ ${DEBUG} == 1 ] && apt update && apt install -y procps net-tools dnsutils iputils-ping curl || true
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Cyclops.MultiCluster.dll"]