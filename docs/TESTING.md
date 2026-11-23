# Testing PxeVice on a site

A checklist for checking PxeVice against a real Configuration Manager site. Use a lab site or a test collection, and a virtual machine that can PXE boot.

## Before you start

- [ ] A virtual machine with a network adapter that can PXE boot, and its MAC address noted
- [ ] A task sequence deployed to a collection with All Unknown Computers, available to Configuration Manager clients, media and PXE
- [ ] A second task sequence deployment that is **required**, to a test collection
- [ ] LogVice installed, to compare with SMSPXE.log

## Connecting

- [ ] PxeVice on the site server connects without Settings, and the subtitle shows the site code, the site database and the PXE distribution points
- [ ] On another PC, with the site server in Settings, it connects the same way
- [ ] The PXE boots tab shows boots from SMSPXE.log. When it does not, the status bar says why, and SMSPXE.log in Settings fixes it
- [ ] Settings lists every PXE distribution point of the site, and the PXE boots tab has boots from all of them
- [ ] A server in **More PXE distribution points** is added to the ones found. With **Find the PXE distribution points from the site** off, only the servers given are used
- [ ] With one PXE distribution point turned off, the others are still read, and the status bar says the slow one comes later
- [ ] **SMSPXE.log** opens the log in LogVice

## PXE boots

- [ ] Turn on **Watch** and PXE boot the virtual machine as an unknown computer. Within the watch time the status bar says a PXE boot came in, and the PXE boots tab shows it with its MAC address, SMBIOS GUID, architecture and result
- [ ] The log lines of the boot match SMSPXE.log in LogVice
- [ ] A boot that was offered a task sequence shows the deployment ID and name
- [ ] A boot that was not serviced is red, and the machine is on the Machines tab

## PXE check

- [ ] **Unknown record**: start the task sequence on the unknown machine and stop it before the computer gets a name (turn the machine off during Apply Operating System). Reboot it from the network. The check shows the leftover Unknown record and the last boot not serviced
- [ ] **Required deployment**: add the device to the collection with the required deployment, let it run, and PXE boot it again. The check shows the required deployment already offered
- [ ] **Clients only**: set a deployment to Only Configuration Manager clients. The check shows it is not available to PXE
- [ ] **Expired**: set an expiry time in the past. The check shows the deployment expired
- [ ] **Boot image**: remove the boot image from the PXE distribution point. The check shows the boot image is not there
- [ ] **Unknown computer support**: turn it off on the distribution point. An unknown machine shows it
- [ ] **Can get**: with everything in place, the check lists the deployment the machine can get
- [ ] **Copy for the ticket** gives the same text as the check
- [ ] **Export** saves an HTML report that opens in the browser, in light and dark mode

## Reset

- [ ] Reset the machine with the Unknown record. The reset page lists the changes: clear the flag, delete the Unknown record, delete the row
- [ ] After the reset Watch starts, and the banner says PxeVice waits for the next PXE boot
- [ ] PXE boot the machine. The status bar and the Activity tab say it was offered the task sequence
- [ ] The device is gone from the console, and `LastPXEAdvertisement` has no row for its MAC address and SMBIOS GUID
- [ ] `%LOCALAPPDATA%\PxeVice\PxeVice.log` has a line for every change and for the next boot, and opens in LogVice
- [ ] A reset with **Restart the PXE service afterwards** restarts WDSServer or SccmPxe on the distribution point
- [ ] Turning on **Delete named device records too** turns on the restart with it. After the reset the Activity tab says `Deleted (decommissioned)` for the device, and it is gone from the console
- [ ] Reset a named device without the restart and PXE boot it at once. If SMSPXE.log answers for the deleted device, the banner says the PXE point still remembers it, and **Restart PXE service** restarts WDSServer or SccmPxe on that distribution point

## Shared adapters

- [ ] PXE boot two virtual machines with the same MAC address and different SMBIOS GUIDs. Resetting one of them leaves the other's PXE flag

## Service account

- [ ] With an account in Settings, **Use it now** asks for the password and connects again. The subtitle says `as <account>`
- [ ] It works with PxeVice started as administrator, and with the Secondary Logon service stopped
- [ ] With a supporter account that has no rights in Configuration Manager, PxeVice reads the site and SMSPXE.log through the service account
- [ ] A reset with the service account writes `<supporter> as <account>` in the activity log
- [ ] A wrong password shows as a failed connection with a hint to check it
- [ ] With **Remember the password**, PxeVice uses the account when it opens, without asking. **Forget the password** makes it ask again
- [ ] **SMSPXE.log** opens a copy in LogVice, read with the account
- [ ] `PxeVice.exe /list` uses the account when the password is remembered, and `/ownaccount` does not
- [ ] On the site server, PxeVice says it connects with your own account

## Sites

- [ ] **Site > Save this site as** saves the site, and **Site** switches between two saved sites
- [ ] `PxeVice.exe /use <name> /list` uses the saved site

## Command line

- [ ] `/list` lists the same machines as the window
- [ ] `/requests` lists the PXE boots, and `/hours 1` only the last hour
- [ ] `/check /mac <mac>` prints the check, with exit code 2 when something blocks PXE and 0 when not
- [ ] `/report <file.html> /mac <mac>` saves the report
- [ ] `/reset /mac <mac> /whatif` lists the changes and changes nothing
- [ ] `/reset /mac <mac>` resets the machine
