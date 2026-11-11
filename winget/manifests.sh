#!/bin/sh
# Writes the winget manifests for one PxeVice release.
# Usage: winget/manifests.sh <version> <PxeVice.msi> <release notes file> <output folder>
set -e

version="$1"
msi="$2"
notes="$3"
out="$4"
if [ -z "$version" ] || [ ! -f "$msi" ] || [ -z "$out" ]; then
  echo "Usage: $0 <version> <PxeVice.msi> <release notes file> <output folder>" >&2
  exit 2
fi

id=DanielBaldissera.PxeVice
repo=Baldissera88/PxeVice
schema=1.12.0
url="https://github.com/$repo/releases/download/v$version/PxeVice.msi"
code=$(msiinfo export "$msi" Property | awk -F'\t' '$1=="ProductCode"{print $2}' | tr -d '\r')
upgrade=$(msiinfo export "$msi" Property | awk -F'\t' '$1=="UpgradeCode"{print $2}' | tr -d '\r')
sha=$(sha256sum "$msi" | cut -d' ' -f1 | tr 'a-f' 'A-F')
date=$(date -u +%Y-%m-%d)
dir="$out/manifests/d/DanielBaldissera/PxeVice/$version"
mkdir -p "$dir"

cat > "$dir/$id.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.version.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
EOF

cat > "$dir/$id.installer.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $version
MinimumOSVersion: 10.0.14393.0
InstallerType: msi
Scope: machine
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '$code'
ReleaseDate: $date
AppsAndFeaturesEntries:
- DisplayName: PxeVice
  Publisher: Daniel L. Baldissera
  ProductCode: '$code'
  UpgradeCode: '$upgrade'
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
EOF

{
  cat <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: Daniel L. Baldissera
PublisherUrl: https://github.com/Baldissera88
PublisherSupportUrl: https://github.com/$repo/issues
Author: Daniel L. Baldissera
PackageName: PxeVice
PackageUrl: https://github.com/$repo
License: MIT
LicenseUrl: https://github.com/$repo/blob/main/LICENSE
Copyright: Copyright (c) 2026 Daniel L. Baldissera
ShortDescription: Clears leftover PXE data in Configuration Manager so a machine can PXE boot again.
Description: |-
  PxeVice finds what a Configuration Manager site keeps after a PXE boot: PXE flags in LastPXEAdvertisement, device records named Unknown, and flags of deleted devices.
  It shows one line per machine with why it is not offered a task sequence, and resets it with Clear Required PXE Deployments and a targeted clean-up, after showing every change.
  Every change is logged in the CMTrace format. A companion to LogVice and PolicyVice.
Moniker: pxevice
Tags:
- configmgr
- endpoint-management
- mecm
- osd
- pxe
- sccm
- task-sequence
- troubleshooting
- wds
- windows
EOF
  if [ -n "$notes" ] && [ -s "$notes" ] && grep -q '[^[:space:]]' "$notes"; then
    echo "ReleaseNotes: |-"
    tr -d '\r' < "$notes" | awk 'NF{found=1} found' | sed -e 's/[[:space:]]*$//' -e 's/^/  /' -e 's/^  $//'
  fi
  cat <<EOF
ReleaseNotesUrl: https://github.com/$repo/releases/tag/v$version
Documentations:
- DocumentLabel: Changelog
  DocumentUrl: https://github.com/$repo/blob/main/CHANGELOG.md
ManifestType: defaultLocale
ManifestVersion: $schema
EOF
} > "$dir/$id.locale.en-US.yaml"

echo "Wrote $dir"
