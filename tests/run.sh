#!/bin/sh
# Builds the non-UI code with every tests/*.cs file and runs the tests under Mono.
set -e
cd "$(dirname "$0")/.."
API=/usr/lib/mono/4.5-api
SRC=$(ls src/*.cs | grep -v -e '/Ui\.' -e '/Program\.cs$' -e '/LogViceLink\.cs$' | sort)
mcs -langversion:5 -target:exe -warn:4 -warnaserror+ -noconfig -nostdlib \
  -r:$API/mscorlib.dll -r:$API/System.dll -r:$API/System.Core.dll -r:$API/System.Data.dll -r:$API/System.Xml.dll -r:$API/System.Management.dll \
  -r:$API/System.ServiceProcess.dll -r:$API/System.Drawing.dll -r:$API/System.Windows.Forms.dll \
  -out:tests/PxeViceTests.exe $SRC src/Ui.Theme.cs tests/*.cs
mono tests/PxeViceTests.exe "$@"
