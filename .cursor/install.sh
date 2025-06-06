#!/bin/bash
# This script installs .NET 8 SDK if not present, runs dotnet restore,
# and then prompts for a package to install with snap.

set -e
# we have to use https://github.com/rhubarb-geek-nz/powershell-ubuntu/releases/download/7.4.5/powershell_7.4.5-1.ubuntu_amd64.deb
# since other builds don't work on ubuntu 25

sudo apt-get update -y
sudo apt-get install wget -y
wget https://github.com/rhubarb-geek-nz/powershell-ubuntu/releases/download/7.4.5/powershell_7.4.5-1.ubuntu_amd64.deb
sudo dpkg -i powershell_7.4.5-1.ubuntu_amd64.deb
sudo apt-get install -f
rm powershell_7.4.5-1.ubuntu_amd64.deb

# --- .NET 8 SDK Installation ---
# Check if .NET 8 SDK is installed. `dotnet --list-sdks` is the most reliable way.
if dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
    echo ".NET 8 SDK is already installed."
else
    echo "Installing .NET 8 SDK..."
    # Using apt is generally recommended for server environments.
    # The commands are from the official Microsoft documentation for Ubuntu.
    sudo apt-get update -y
    sudo apt-get install -y dotnet-sdk-8.0
fi

echo "--- Restoring .NET dependencies ---"
# Assuming the script is run from the root of the repository.
if [ -f "Imageflow.Server.sln" ]; then
    dotnet restore Imageflow.Server.sln
else
    echo "Imageflow.Server.sln not found. Skipping dotnet restore."
    echo "Please run this script from the root of the wimageflow-dotnet-server repository."
fi

# install just
cargo install just