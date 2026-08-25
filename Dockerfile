FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /source

# Copy everything
COPY . ./

# Restore and build
RUN dotnet restore
RUN dotnet publish src/JoaArtifactsMMOClient/JoaArtifactsMMOClient.csproj -c release -o /app

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "JoaArtifactsMMOClient.dll"]