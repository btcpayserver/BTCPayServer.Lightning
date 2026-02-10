#!/bin/bash
set -euo pipefail
rm -rf "bin/Release/"
dotnet pack BTCPayServer.Lightning.Common.csproj --configuration Release --include-symbols -p:SymbolPackageFormat=snupkg
package=$(find ./bin/Release -name "*.nupkg" -type f | head -n 1)
dotnet nuget push "$package" --source "https://api.nuget.org/v3/index.json" --api-key "$NUGET_API_KEY"
ver=$(basename "$package" | sed -E 's/[^0-9]*\.([0-9]+(\.[0-9]+){1,4}).*/\1/')
git tag -a "Common/v$ver" -m "Common/$ver"
git push --tags
