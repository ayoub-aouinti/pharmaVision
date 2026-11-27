FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish src/$PROJECT/$PROJECT.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV APP_DLL=$PROJECT.dll
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL"]
