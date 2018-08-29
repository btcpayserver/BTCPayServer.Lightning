Remove-Item "bin\release\" -Recurse -Force
dotnet pack --configuration Release
dotnet nuget push "bin\Release\*.nupkg" --source "https://api.nuget.org/v3/index.json"
$ver = ((Get-ChildItem .\bin\release\*.nupkg)[0].Name -replace '[^\d]*\.(\d+(\.\d+){1,4}).*', '$1')
git tag -a "Charge/v$ver" -m "Charge/$ver"
git push --tags
