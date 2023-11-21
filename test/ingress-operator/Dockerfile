#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Vecc.IngressOperator.csproj", "."]
RUN dotnet restore "./Vecc.IngressOperator.csproj"
COPY . .
RUN dotnet build "Vecc.IngressOperator.csproj" -c Release -o /app/build/
RUN dotnet publish "Vecc.IngressOperator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Vecc.IngressOperator.dll"]