#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj", "src/Vecc.K8s.MultiCluster.Api/"]
RUN dotnet restore "src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj"
COPY . .
RUN dotnet build "src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj" -c Release

FROM build AS publish
RUN dotnet publish "src/Vecc.K8s.MultiCluster.Api/Vecc.K8s.MultiCluster.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
ARG DEBUG=0
ENV DEBUG=${DEBUG}
SHELL [ "/bin/bash", "-c" ]
RUN [ ${DEBUG} == 1 ]  && apt update && apt install -y procps net-tools dnsutils || true
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Vecc.K8s.MultiCluster.Api.dll"]