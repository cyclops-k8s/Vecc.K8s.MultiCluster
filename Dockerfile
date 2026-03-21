#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj", "src/Vecc.K8s.MultiCluster.Api/"]
COPY ["src/Vecc.K8s.MultiCluster.Api.Tests/Vecc.K8s.MultiCluster.Api.Tests.csproj", "src/Vecc.K8s.MultiCluster.Api.Tests/"]
RUN dotnet restore "src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj"
RUN dotnet restore "src/Vecc.K8s.MultiCluster.Api.Tests/Vecc.K8s.MultiCluster.Api.Tests.csproj"
COPY . .
RUN dotnet build "src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj" -c Release
RUN dotnet build "src/Vecc.K8s.MultiCluster.Api.Tests/Vecc.K8s.MultiCluster.Api.Tests.csproj" -c Release
RUN dotnet test "src/Vecc.K8s.MultiCluster.Api.Tests/Vecc.K8s.MultiCluster.Api.Tests.csproj" -c Release --no-build

FROM build AS publish
RUN dotnet publish "src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
ARG DEBUG=0
ENV DEBUG=${DEBUG}
SHELL [ "/bin/bash", "-c" ]
RUN [ ${DEBUG} == 1 ] && apt update && apt install -y procps net-tools dnsutils iputils-ping curl || true
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Vecc.K8s.MultiCluster.Api.dll"]