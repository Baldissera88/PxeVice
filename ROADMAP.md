# Roadmap

What is planned for the next versions of PxeVice.

Released in 2.0.0: Overview, Back and Forward, Settings by subject, PXE health, tickets, a file before a device is deleted, policies for administrators, boot keys for the user, and releases built by GitHub Actions. Released in 0.5.0 to 0.8.0: the themes and Help of TrackVice, plain-words diagnosis, high risk device deletion and updates. Released in 0.3.x: PXE boots from SMSPXE.log, the PXE check, Watch, saved sites, reports and the service account. See CHANGELOG.md.

Wishes are welcome: [Discussions](https://github.com/Baldissera88/PxeVice/discussions) or an issue on GitHub.

The first four change a production site or need a certificate, so they wait for a lab to test them on.

## 1. Add a machine to a collection

When the PXE check says that no task sequence is deployed to the machine, the fix is often to put it in a collection that has one.

- From that finding, **Add to collection**: choose a collection that has a task sequence deployed, see what it will add, and add the machine as a direct member. It asks first, shows the change and is in the activity log.
- Only collections the account can change are offered, and only direct membership is used.

## 2. Reset from a list

A lab of 30 PCs that is re-imaged every term: paste the names or MAC addresses, check what PxeVice found for each, and reset them in one go, within the limit of `MaxMachinesPerReset`.

## 3. Signed exe and MSI, and a signature check for updates

Today SmartScreen says "Unknown publisher" on a downloaded PxeVice.msi, and AppLocker or WDAC need your own certificate.

- Sign PxeVice.exe and PxeVice.msi in the GitHub workflow, with a timestamp.
- The updater checks the signature of a downloaded file as well as its checksum and the build attestation.

## 4. Restore flags from the file

The file written before a deletion holds the `LastPXEAdvertisement` rows. Offer to put them back. Device records cannot be made again, so the file stays a record for those.

## 5. Waiting window and active clients

- After a reset, say how long PxeVice waits for the next PXE boot, and when to give up.
- Warn when a deleted device still has an active client, since it registers again.

## 6. The same page in every Vice app

Move the Overview, Settings and Help code that PxeVice and TrackVice share into one place, so a fix in one reaches the others.
