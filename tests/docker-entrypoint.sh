#!/bin/sh
set -e

dotnet test -c Release -v n --logger "console;verbosity=normal" < /dev/null
