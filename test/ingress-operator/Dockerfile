#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Cyclops.IngressOperator.csproj", "."]
RUN dotnet restore "./Cyclops.IngressOperator.csproj"
COPY . .
RUN dotnet build "Cyclops.IngressOperator.csproj" -c Release -o /app/build/
RUN dotnet publish "Cyclops.IngressOperator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Cyclops.IngressOperator.dll"]