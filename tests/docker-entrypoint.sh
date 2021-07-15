#!/bin/sh
set -e

dotnet tool restore
dotnet test -c Release -v n < /dev/null
