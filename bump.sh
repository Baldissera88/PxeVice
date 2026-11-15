#!/bin/sh
# Sets a new PxeVice version: PxeVice.wxs (with a new product code) and src/AssemblyInfo.cs,
# and adds an empty dated section for it at the top of CHANGELOG.md.
# Usage: ./bump.sh <major.minor.patch>
set -e
cd "$(dirname "$0")"

new="$1"
if ! printf '%s\n' "$new" | grep -Eq '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'; then
  echo "Usage: $0 <major.minor.patch>, for example $0 1.2.0" >&2
  exit 2
fi

current=$(sed -n 's/^ *Version="\([^"]*\)".*/\1/p' PxeVice.wxs | head -n 1)
if [ -z "$current" ]; then
  echo "No Version was found in PxeVice.wxs." >&2
  exit 1
fi

# The highest of the two versions comes last when sorted by number, part by part.
highest=$(printf '%s\n%s\n' "$current" "$new" | sort -t. -k1,1n -k2,2n -k3,3n | tail -n 1)
if [ "$new" = "$current" ] || [ "$highest" != "$new" ]; then
  echo "$new is not higher than the current version $current." >&2
  exit 1
fi
if grep -q "^## PxeVice $new\$" CHANGELOG.md; then
  echo "CHANGELOG.md already has a section for $new." >&2
  exit 1
fi

if [ -r /proc/sys/kernel/random/uuid ]; then
  guid=$(cat /proc/sys/kernel/random/uuid)
else
  guid=$(uuidgen)
fi
guid=$(printf '%s' "$guid" | tr 'a-f' 'A-F')

sed -i \
  -e "s/<Product Id=\"[^\"]*\"/<Product Id=\"$guid\"/" \
  -e "s/^\( *\)Version=\"[^\"]*\"/\1Version=\"$new\"/" \
  -e "s/DefaultVersion=\"[^\"]*\"/DefaultVersion=\"$new.0\"/" \
  PxeVice.wxs
sed -i \
  -e "s/AssemblyVersion(\"[^\"]*\")/AssemblyVersion(\"$new.0\")/" \
  -e "s/AssemblyFileVersion(\"[^\"]*\")/AssemblyFileVersion(\"$new.0\")/" \
  -e "s/AssemblyInformationalVersion(\"[^\"]*\")/AssemblyInformationalVersion(\"$new\")/" \
  src/AssemblyInfo.cs

date=$(LC_ALL=C date +'%-d %B %Y')
awk -v v="$new" -v d="$date" '
  !done && /^## / { print "## PxeVice " v; print ""; print d "."; print ""; done = 1 }
  { print }
' CHANGELOG.md > CHANGELOG.md.new
mv CHANGELOG.md.new CHANGELOG.md

for f in PxeVice.wxs src/AssemblyInfo.cs; do
  if ! grep -q "$new" "$f"; then
    echo "$f was not changed. Check it by hand." >&2
    exit 1
  fi
done

echo "PxeVice $current -> $new, product code {$guid}"
echo "Changed PxeVice.wxs, src/AssemblyInfo.cs and CHANGELOG.md."
echo "Write the changes in CHANGELOG.md, then build, test and commit."
