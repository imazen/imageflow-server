set shell := ["pwsh", "-c"]

# Run tests for the ImazenShared.Tests project using dotnet run
test-shared *ARGS:
    dotnet run --no-restore --project tests/ImazenShared.Tests/ImazenShared.Tests.csproj -- -m 16 {{ARGS}} 

test-all *ARGS:
    dotnet test --no-restore -- -m 16

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

