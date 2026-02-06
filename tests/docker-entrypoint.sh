#!/bin/sh
set -e

dotnet build -c Release
dotnet test -c Release --no-build -v n --filter "Category!=LndTestListener" --logger "console;verbosity=normal" < /dev/null
dotnet test -c Release --no-build -v n --filter "Category=LndTestListener" --logger "console;verbosity=normal" < /dev/null
