# Build Aşaması
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Tüm dosyaları kopyala
COPY . .

# Projeyi restore et ve derle
RUN dotnet restore "HacettepeNots/HacettepeNots.csproj"
RUN dotnet publish "HacettepeNots/HacettepeNots.csproj" -c Release -o /app/publish

# Çalıştırma Aşaması
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Çıktı DLL adı
ENTRYPOINT ["dotnet", "HacettepeNots.dll"]
