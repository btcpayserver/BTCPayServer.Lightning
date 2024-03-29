Remove-Item "bin\release\" -Recurse -Force
dotnet pack BTCPayServer.Lightning.Common.csproj --configuration Release --include-symbols -p:SymbolPackageFormat=snupkg
$package=(ls .\bin\Release\*.nupkg).FullName
dotnet nuget push $package --source "https://api.nuget.org/v3/index.json"
$ver = ((Get-ChildItem .\bin\release\*.nupkg)[0].Name -replace '[^\d]*\.(\d+(\.\d+){1,4}).*', '$1')
git tag -a "Common/v$ver" -m "Common/$ver"
git push --tags
