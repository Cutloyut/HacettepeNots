# Build Aşaması
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Tüm dosyaları kopyala
COPY . .

# Projeyi restore et ve derle
RUN dotnet restore "HacettepeNot.csproj"
RUN dotnet publish "HacettepeNot.csproj" -c Release -o /app/publish

# Çalıştırma Aşaması
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Çıktı DLL adı tam olarak HacettepeNot.dll
ENTRYPOINT ["dotnet", "HacettepeNot.dll"]