# build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["AksaDigitex.CFO.API.csproj", "."]
RUN dotnet restore "AksaDigitex.CFO.API.csproj"

COPY . .
RUN dotnet build "AksaDigitex.CFO.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AksaDigitex.CFO.API.csproj" -c Release -o /app/publish

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AksaDigitex.CFO.API.dll"]