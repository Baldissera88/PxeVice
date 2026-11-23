# Roadmap

What is planned for the next versions of PxeVice.

Released in 0.3.1: PXE boots from SMSPXE.log, the PXE check, Watch, next boot after a reset, saved sites, reports, the service account and every PXE distribution point. See CHANGELOG.md.

Wishes are welcome: [Discussions](https://github.com/Baldissera88/PxeVice/discussions) or an issue on GitHub.

## 1. Deleted devices: check, wait and explain

Deleting a named device only marks it Decommissioned. It is gone from the console at once, but the PXE point still remembers the device for a while, and the database row stays until Delete Aged Discovery Data runs.

- After a device is deleted, PxeVice reads the site again and says whether the device is really gone, or that it is only decommissioned.
- Turning on "Delete named device records too" turns on "Restart the PXE service" with it, because that empties the PXE point's memory of the device. It can still be turned off.
- If the next PXE boot is still answered for the deleted device (its resource ID in SMSPXE.log), PxeVice says that it is the PXE point's cache, and offers to restart the PXE service on that distribution point.
- The Activity tab and the report say "Deleted (decommissioned)" and not only "Deleted".

## 2. Find the machine without the MAC address

A supporter often has only a name, a serial number or a user.

- Search in Machines on name, MAC address, SMBIOS GUID, serial number and the user from the device record, with a short hint of what was matched.
- A button on a PXE boot in the PXE boots tab: **Reset this machine**. It opens the reset for the machine behind that boot, so the supporter can ask the user to PXE boot once and then reset what just came in.
- "Last PXE boot that was not serviced" as a ready filter, newest first, since it is almost always the machine the supporter is looking for.

## 3. A ticket number with every change

- A field in the reset: **Ticket / reference** (optional, can be made required in Settings). It is written to the activity log, the Activity tab and the report.
- The summary for the ticket gets one more button: **Copy for a ticket**, a short plain text with the machine, what blocked PXE, what was changed, by whom and with which account, and the time.

## 4. Undo: save what is deleted

- Before a reset deletes anything, PxeVice saves the rows it will remove (`LastPXEAdvertisement` rows and the key data of the device records: name, resource ID, MAC address, SMBIOS GUID, collections) to a file next to the activity log.
- **Open the last reset** shows what was changed. Rows from `LastPXEAdvertisement` can be put back from the file. Device records cannot be made again, so the file is there to see what was removed.

## 5. PXE health of the site

The PXE check looks at one machine. This looks at the PXE points.

- A page that lists every PXE distribution point and says for each: the PXE responder and WDS services are running, SMSPXE.log has no recent errors, the boot images are on the point and up to date, the PXE certificate is valid, unknown computer support is on, and the point has answered a PXE boot lately.
- Findings in the same worst-first order as the PXE check, with what to do.
- Command line: `/health`, with an exit code that says when something is wrong, so it can run as a scheduled check.

## 6. Overview as the home page

- Like TrackVice: a first page with how many PXE boots came in the last 24 hours, how many were not serviced, how many machines have something left, and the most common reasons.
- The numbers open the list behind them. A recent machines list with the last ones that were reset.

## 7. Get a known machine ready for PXE

When the PXE check says that no task sequence is deployed to the machine, the fix is often to put it in a collection that has one.

- From a finding "no task sequence deployed to the machine", offer **Add to collection**: choose a collection that has a task sequence deployed (the ones PxeVice already finds), see what it will add, and add the machine as a direct member. Asks first, shows the change, and is in the activity log.
- Only collections that the account can change are offered, and only direct membership is used.

## 8. Roles for supporters

- A read-only mode, set per site, so some supporters can look and not change anything. The buttons that change things are gone and the command line refuses `/reset`.
- A limit in Settings on how many machines one reset can touch.
- The service account and these settings can be put in a file in the install folder, so IT sets them once for all supporters.

## 9. Help, like in TrackVice

- Help > **Report a problem** (copies what is useful: version, site settings without passwords, the last lines of the activity log), **Suggest a feature** and **Tell the author**.
- A "What you need" page in Settings that says which rights the account needs on the SMS Provider, the site database and the PXE distribution points, and what each part is used for.

## 10. The window at small sizes

- Check every tab, Settings and the dialogs at 900 x 600 and at 125 % and 150 % scaling, with the grid, the detail text and the buttons.
- The toolbar folds into a "More" menu when there is no room.
