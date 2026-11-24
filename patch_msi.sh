#!/bin/sh
# wixl writes 8.3 names only. This adds the long names to the Directory, File and Shortcut tables,
# and adds what wixl leaves out of PxeVice.wxs: closing a running PxeVice before an upgrade.
set -e
msi="$1"
work=$(mktemp -d)
cd "$work"
for t in Directory File Shortcut CustomAction InstallExecuteSequence; do msiinfo export "$msi" $t > $t.idt; done
python3 - <<'PY'
def patch(name, fn):
    lines = open(name + '.idt', newline='').read().split('\r\n')
    out = lines[:3] + [fn(l.split('\t')) for l in lines[3:] if l] + ['']
    open(name + '.idt', 'w', newline='').write('\r\n'.join(out))
def d(c):
    if c[0] == 'INSTALLDIR': c[2] = 'PXEVICE|PxeVice'
    return '\t'.join(c)
def f(c):
    if c[0] == 'PxeViceExe': c[2] = 'PXEVICE.EXE|PxeVice.exe'
    return '\t'.join(c)
def s(c):
    if c[0] == 'StartMenuShortcut':
        c[2] = 'PXEVICE|PxeVice'
        c[4] = '[#PxeViceExe]'
    return '\t'.join(c)
patch('Directory', d); patch('File', f); patch('Shortcut', s)

# wixl leaves out the ClosePxeVice action from PxeVice.wxs and ignores MajorUpgrade Schedule.
# Add the action (34 exe in a directory + 64 ignore exit code + 1024 deferred + 2048 no impersonation),
# and run it right after InstallInitialize, before the files are installed. The old version is removed after
# InstallFinalize: Windows Installer allows no action between InstallInitialize and RemoveExistingProducts (error 2613).
ca = open('CustomAction.idt', newline='').read()
if 'ClosePxeVice' not in ca:
    with open('CustomAction.idt', 'a', newline='') as fh:
        fh.write('\t'.join(['ClosePxeVice', '3170', 'SystemFolder', 'taskkill.exe /F /IM PxeVice.exe', '']) + '\r\n')
seq = {'ClosePxeVice': '1501', 'RemoveExistingProducts': '6700'}
def q(c):
    if c[0] in seq: c[2] = seq[c[0]]
    return '\t'.join(c)
patch('InstallExecuteSequence', q)
PY
msibuild "$msi" -i Directory.idt File.idt Shortcut.idt CustomAction.idt InstallExecuteSequence.idt
rm -rf "$work"
