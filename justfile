set shell := ["pwsh", "-c"]

# Run tests for the ImazenShared.Tests project using dotnet run
test-shared *ARGS:
    dotnet run --project tests/ImazenShared.Tests/ImazenShared.Tests.csproj -- {{ARGS}} 