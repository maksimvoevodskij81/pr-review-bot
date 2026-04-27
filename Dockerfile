FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY pr-review-bot.csproj .
RUN dotnet restore --packages /nuget
COPY . .
RUN dotnet publish pr-review-bot.csproj -c Release -o /app --packages /nuget

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 3000
ENV ASPNETCORE_URLS=http://+:3000
ENTRYPOINT ["dotnet", "PrReviewBot.dll"]