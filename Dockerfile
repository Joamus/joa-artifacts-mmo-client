FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy everything
COPY . ./

# Restore and build
RUN dotnet restore
RUN dotnet publish src/JoaArtifactsMMOClient/JoaArtifactsMMOClient.csproj -c release -o /app

# Setup DNS
RUN echo "nameserver 8.8.8.8" >> /etc/resolv.conf

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "JoaArtifactsMMOClient.dll"]