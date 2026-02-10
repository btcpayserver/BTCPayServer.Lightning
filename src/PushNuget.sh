#!/bin/bash
set -euo pipefail
cd "BTCPayServer.Lightning.Common"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.CLightning"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.Charge"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.LNDhub"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.LND"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.Eclair"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.LNbank"
./PushNuget.sh
cd ..

cd "BTCPayServer.Lightning.All"
./PushNuget.sh
cd ..
