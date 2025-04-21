#!/bin/bash

# compile-bicep.sh

# Ensure Azure CLI and Bicep are installed
az bicep install

# Compile the main Bicep template
az bicep build --file ../main.bicep --out ../main.json

echo "Bicep template compiled to ARM JSON."
