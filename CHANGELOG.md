# Changelog

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
