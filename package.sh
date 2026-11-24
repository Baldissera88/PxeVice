#!/bin/sh
set -e
cd "$(dirname "$0")"
./build.sh
wixl -a x64 -o PxeVice.msi PxeVice.wxs
./patch_msi.sh "$PWD/PxeVice.msi"
echo PACKAGE_OK
