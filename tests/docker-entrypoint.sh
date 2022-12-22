#!/bin/sh
set -e

dotnet build -c Release
dotnet test -c Release --no-build -v n --logger "console;verbosity=normal" < /dev/null
