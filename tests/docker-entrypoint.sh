#!/bin/sh
set -e

dotnet test -c Release -v n < /dev/null
