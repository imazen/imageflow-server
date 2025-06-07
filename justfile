set shell := ["pwsh", "-c"]

# Run tests for the ImazenShared.Tests project using dotnet run
#  just test-shared -stopOnFail
test-shared *ARGS:
    dotnet run -p:MaxCpuCount=16 --no-restore --project tests/ImazenShared.Tests/ImazenShared.Tests.csproj  --  {{ARGS}} 

test-shared-fast *ARGS:
    just test-shared -stopOnFail

test-shared-with-48 *ARGS:
    $env:TEST_DOTNET_48 = 'true'
    just test-shared {{ARGS}}
    $env:TEST_DOTNET_48 = 'false'

test-all *ARGS:
    dotnet test -p:MaxCpuCount=16 --no-restore --no-build -- {{ARGS}}

test-all-fast *ARGS:
    just test-all -stopOnFail

test-all-with-48 *ARGS:
    $env:TEST_DOTNET_48 = 'true'; just test-all {{ARGS}}; $env:TEST_DOTNET_48 = 'false';

run-example-minimal *ARGS:
    dotnet run --project examples/Imageflow.Server.ExampleMinimal/Imageflow.Server.ExampleMinimal.csproj -- {{ARGS}}

run-example-docker *ARGS:
    dotnet run --project examples/Imageflow.Server.ExampleDocker/Imageflow.Server.ExampleDocker.csproj -- {{ARGS}}

run-example-docker-with-cache *ARGS:
    dotnet run --project examples/Imageflow.Server.ExampleDockerDiskCache/Imageflow.Server.ExampleDockerDiskCache.csproj -- {{ARGS}}

run-example-modern-api *ARGS:
    dotnet run --project examples/Imageflow.Server.ExampleModernAPI/Imageflow.Server.ExampleModernAPI.csproj -- {{ARGS}}

run-example-full *ARGS:
    dotnet run --project examples/Imageflow.Server.Example.csproj -- {{ARGS}}

run-zrio *ARGS:
    dotnet run --project examples/ZRIO/zrio.csproj -- {{ARGS}}

run-server-host *ARGS:
    dotnet run --project src/Imageflow.Server.Host/Imageflow.Server.Host.csproj -- {{ARGS}}

combine-shared-tests:
    $outputFile = 'tests/ImazenShared.Tests/shared-combined.txt.cs'; Clear-Content $outputFile -ErrorAction SilentlyContinue; Get-ChildItem -Path tests/ImazenShared.Tests/ -Include *.cs, *.md -Recurse | ForEach-Object { $commentPrefix = '//'; if ($_.Extension -eq '.md') { $commentPrefix = '#' }; Add-Content -Path $outputFile -Value "$commentPrefix File: $($_.FullName.Replace($PWD.Path + '\', ''))"; Add-Content -Path $outputFile -Value (Get-Content -Raw $_.FullName); Add-Content -Path $outputFile -Value "`n`n" }

combine-all-routing-syntax:
    $outputFile = 'routing-all-combined.txt.cs'; Clear-Content $outputFile -ErrorAction SilentlyContinue; Get-ChildItem -Path tests/ImazenShared.Tests/Routing/,src/Imazen.Routing/Matching/,src/Imazen.Routing/Parsing/ -Include *.cs, *.md -Recurse | ForEach-Object { $commentPrefix = '//'; if ($_.Extension -eq '.md') { $commentPrefix = '#' }; Add-Content -Path $outputFile -Value "$commentPrefix File: $($_.FullName.Replace($PWD.Path + '\', ''))"; Add-Content -Path $outputFile -Value (Get-Content -Raw $_.FullName); Add-Content -Path $outputFile -Value "`n`n" }

# look recursively for bin and obj folders and delete them
delete-bin-obj:
    Get-ChildItem -Path . -Include bin, obj -Recurse -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -Recurse -Force -Path $_.FullName }

# delete all lock files
delete-lock-files:
    Get-ChildItem -Path . -Include *.lock.json -Recurse -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -Force -Path $_.FullName }

