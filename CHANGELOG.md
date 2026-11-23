# Changelog

## PxeVice 0.6.1

23 November 2026.

- **No deletion of devices that share an SMBIOS GUID**: some firmware reports the same SMBIOS GUID on many machines. When several named devices matched a machine only by SMBIOS GUID, a reset with **Delete the device from ConfigMgr** on would have deleted all of them. They are now left alone, as already happened for a shared MAC address, and the reset says which devices it skipped. A device that has its own PXE flag is still matched by its resource ID

## PxeVice 0.6.0

23 November 2026.

- **Delete a device that has no deployment, shown as high risk**: when a device is in the site but no task sequence is deployed to it, the last PXE boot is "Not serviced". Deleting the device makes the next PXE boot an unknown computer, so the deployment to All Unknown Computers applies. The reset dialog now says this for the machine, and the option **Delete the device from ConfigMgr** is red with a **High risk** tag, off by default
- The step in the list of changes is red and says it also removes the collection memberships. Pressing Reset with a device deletion asks one more time, with the device names, and No is the default
- The check says the same: deleting the device is a last resort, and a task sequence must be deployed to All Unknown Computers first

## PxeVice 0.5.3

23 November 2026.

- **Restart as administrator**: when the PXE service cannot be restarted because PxeVice is not running as administrator, a banner says so and has a button that starts PxeVice again with the Windows prompt for administrator rights, with the same options. A service account only applies to other computers, so on the PXE server itself PxeVice needs to run as administrator. The banner goes away after a reset that went well

## PxeVice 0.5.2

23 November 2026.

- **Install no longer needs SHA256SUMS.txt**: when a release has no `SHA256SUMS.txt`, PxeVice checks the download against the SHA-256 that GitHub gives for each file of the release. With neither, there is no Install button, and the update page now says why instead of leaving it out
- The **Update** button (banner and Settings > Updates, called What is new before) opens the page with the release notes and **Install**. Settings > Updates says in the version line why Install is not offered

## PxeVice 0.5.1

23 November 2026.

- **Clear message when the PXE service cannot be restarted**: Windows only says "Cannot open WDSServer service on computer", and the cause is in the inner error. PxeVice now shows it, and when it is missing rights it says that restarting the PXE service needs administrator rights on that server: start PxeVice as administrator when it runs on the server, or use a service account that is a local administrator there. Other failed changes show the inner error too
- **A device the site no longer has** gives "The site no longer has this device, so there is nothing to clear" for Clear Required PXE Deployments, instead of "Not found" as a failure. The reset goes on and deletes the rows that are left

## PxeVice 0.5.0

23 November 2026.

- **The themes of TrackVice**: three new themes, **Terminal** (green on black, like an old phosphor screen), **Amber** (amber on black) and **Synthwave** (dark purple with neon pink and cyan). Terminal and Amber have square corners and show the counts in a monospace font. The colours are the same as in TrackVice
- **Vice is the default** on a new install. A theme you have chosen stays
- Settings shows every theme as a tile with a small colour sample, instead of the four-way switch. The Theme button in the toolbar steps through all of them
- `--theme terminal|amber|synthwave|vice|dark|light|system` starts PxeVice with a theme for that run only
- Light's secondary text is darker and easier to read

## PxeVice 0.4.0

23 November 2026.

- **PxeVice tells when there is a new version**: once a day when it opens, it asks GitHub for the latest release of PxeVice and shows a banner when there is a newer one, with **What is new**: the release notes, a **Release page** button and, when installing is turned on, **Install**. Nothing about you or the site is sent. `/checkupdate` on the command line does the same check
- **Install from PxeVice** is off until it is turned on in Settings > Updates. PxeVice then downloads the MSI (or `PxeVice.exe` when it is not installed with the MSI), checks it against `SHA256SUMS.txt` from the release, closes, installs and opens again. The MSI asks for administrator rights, so a supporter without them uses the release page
- **Settings > Updates**: turn the check on or off, turn Install on or off, Check now and What is new
- **For the organization**: the policy value `DisableUpdateCheck` under `HKLM\SOFTWARE\Policies\PxeVice` turns the check off for everyone, and `DisableUpdateInstall` turns Install off. Each release must have `PxeVice.msi`, `PxeVice.exe` and `SHA256SUMS.txt` for Install to be offered

## PxeVice 0.3.2

23 November 2026.

- **Deleted devices are checked**: after PxeVice deletes a named device it reads the site again, and the Activity tab says `Deleted (decommissioned)`. If the site still lists the device, the change is shown as failed, so it is not taken for done. Deleting only marks a device decommissioned: it is gone from the console at once and removed from the database later by Delete Aged Discovery Data
- **The PXE service is restarted with a deleted device**: turning on **Delete named device records too** turns on **Restart the PXE service afterwards** with it, since the PXE point remembers a device for a while. It can be turned off again. On the command line `/removedevice` restarts the PXE service too, and `/keeppxe` stops that
- **When the PXE point still remembers**: if the next PXE boot is answered for the device that was deleted, PxeVice says that the PXE point still remembers it, and the banner has **Restart PXE service** for that distribution point

## PxeVice 0.3.1

16 November 2026.

- **Fixed: the service account gave "Access is denied"**. PxeVice no longer starts itself again with the account, which needed the Secondary Logon service and was refused with "Access is denied" on some PCs. It now signs in with the account for the network inside PxeVice, like `runas /netonly`: WMI gets the account directly, and the site database, SMSPXE.log and the PXE service are reached as the account
- **Use it now** in Settings asks for the password and connects again with the account, without restarting PxeVice
- SMSPXE.log opens in LogVice as a copy read with the service account, since LogVice runs with your own account
- The command line uses the service account when its password is remembered. `/ownaccount` uses your own account

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
