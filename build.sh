#!/bin/bash
# CrudKit build, test, and pack script (Linux/macOS)

set -e

CONFIGURATION="${1:-Release}"
VERSION="${2:-1.0.0}"
OUTPUT_DIR="./nupkg"

echo "=== CrudKit Build ==="
echo "Configuration: $CONFIGURATION"
echo "Version: $VERSION"

# Clean
echo -e "\n--- Clean ---"
dotnet clean CrudKit.slnx -c "$CONFIGURATION" --verbosity quiet
rm -rf "$OUTPUT_DIR"

# Build
echo -e "\n--- Build ---"
dotnet build CrudKit.slnx -c "$CONFIGURATION" -p:Version="$VERSION"

# Test
echo -e "\n--- Test ---"
dotnet test CrudKit.slnx -c "$CONFIGURATION" --no-build --verbosity minimal
echo "All tests passed!"

# Pack
echo -e "\n--- Pack ---"
dotnet pack CrudKit.slnx -c "$CONFIGURATION" -p:Version="$VERSION" -o "$OUTPUT_DIR" --no-build

echo -e "\nPackages created:"
ls -lh "$OUTPUT_DIR"/*.nupkg

echo -e "\n=== Done ==="
echo "To push: dotnet nuget push ./nupkg/*.nupkg --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json"
