# compile-bicep.ps1

# Ensure Azure CLI and Bicep are installed
az bicep install

# Compile the main Bicep template
$mainBicepPath = "..\main.bicep"
$mainJsonPath = "..\main.json"

az bicep build --file $mainBicepPath --outFile $mainJsonPath

Write-Host "Bicep template compiled to ARM JSON."
