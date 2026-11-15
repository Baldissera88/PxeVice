# Changelog

## PxeVice 0.3.0

15 November 2026.

- **Service account**: set an account in Settings, and PxeVice connects to the SMS Provider, the site database and the PXE distribution points with it, like `runas /netonly`. On the PC the supporter is still themselves. Supporters need no rights of their own in Configuration Manager or the site database
- The password is asked for when PxeVice starts, or remembered for the Windows user, encrypted with DPAPI. **Forget the password** removes it
- The activity log, the Activity tab and reports say both who made a change and the account it was made with, such as `CONTOSO\supporter as CONTOSO\svc-pxevice`
- The subtitle shows the account PxeVice runs with, and a failed sign-in says to check its password
- Run on the site server itself, PxeVice says that it connects with your own account, since the service account is only used over the network
- **Every PXE distribution point**: PxeVice finds all distribution points with PXE turned on from the site, also when you add servers in Settings. **Find the PXE distribution points from the site** can be turned off to use only the servers you give, and Settings lists the ones found
- SMSPXE.log is read on all PXE distribution points at the same time, so a slow or unreachable server does not hold up the rest. With more than four, less of each log is read when PxeVice connects

## PxeVice 0.2.0

15 November 2026.

- **PXE boots**: PxeVice reads SMSPXE.log on every PXE distribution point and shows each PXE boot: when, from which MAC address and SMBIOS GUID, the architecture, whether the site knew the machine, and what it got: a task sequence, or not serviced. The log lines behind it are one click away. A supporter finds the machine that just failed without knowing its MAC address
- Machines whose last PXE boot was not serviced are on the Machines tab too, with the device record they booted as, even when nothing is left in `LastPXEAdvertisement`
- **PXE check**: for every machine, what blocks it and what to do, worst first: a required deployment already offered, a leftover Unknown record, several device records, a duplicate SMBIOS GUID or MAC address, no task sequence deployed to the machine or to Unknown Computers, a deployment only available to Configuration Manager clients, not available yet or expired, a boot image that is not on the PXE distribution point, and unknown computer support turned off. What works shows too: the deployments it can get
- **Watch** (Ctrl+W) reads the site and the new lines of SMSPXE.log every 10 seconds and says when a PXE boot comes in
- **Next boot after a reset**: PxeVice starts watching, waits for the machine to PXE boot again, and says in the status bar and on the Activity tab whether it was offered a task sequence
- **Saved sites**: switch between sites from the toolbar, and use one on the command line with `/use`
- **Reports for the ticket**: an HTML report of the selected machines with the check, the PXE boots and the changes made (Ctrl+E), the check as text on the clipboard, and the PXE boots as CSV
- Command line: `/check` (exit code 2 when something blocks PXE), `/requests` and `/report`
- Settings: how often Watch reads, watching after a reset or at start, how far back PXE boots are shown, and where SMSPXE.log is when PxeVice does not find it
- SMSPXE.log is found in `SMS_DP$\sms\logs`, the site's Logs folder, the Configuration Manager install folder and `SMS_CCM\Logs`
- `bump.sh` and `bump.ps1` set a new version, like in TrackVice

## PxeVice 0.1.0

11 November 2026. First version.

- **Machines**: one line per machine with what is left after a PXE boot: PXE flags in `LastPXEAdvertisement`, device records named `Unknown`, and flags of deleted devices. Matched on the SMBIOS GUID, or on the MAC address when there is no GUID, newest first
- Why each machine does not PXE boot, in words, with the deployment and task sequence name
- **Reset**: clears the PXE flag with Clear Required PXE Deployments, deletes Unknown device records, deletes the machine's rows that are left in `LastPXEAdvertisement` and, when chosen, restarts the PXE service. Every change is shown before it is made
- Safe with shared USB network adapters: with an SMBIOS GUID, other records only match on the MAC address when they have no GUID. Without one, devices that share the MAC address are left alone, and more than 25 rows for one machine are never deleted
- **PXE flags** tab with every row in `LastPXEAdvertisement` and what it belongs to
- **Activity** tab, and an activity log in the CMTrace format in `%LOCALAPPDATA%\PxeVice`
- Opens SMSPXE.log from the PXE distribution point in LogVice
- Finds the site code, site database and PXE distribution points from the SMS Provider
- Command line: `/list` and `/reset` by MAC address, SMBIOS GUID or name, with `/whatif`
- Light, Dark and Vice themes, the same as LogVice and PolicyVice
