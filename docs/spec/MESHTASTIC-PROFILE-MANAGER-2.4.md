# Meshtastic Profile Manager 2.4

## Goal

Provide an organizer-facing profile editor and a participant-facing apply/verify workflow without copying device identity or private keys by default.

## Profile layers

```text
Universal Profile
      |
      +--> T-Beam override
      +--> T-Beam Supreme override
      +--> MeshTracker X1 override
      |
      v
Final device profile
```

## Apply contract

1. discover radio
2. connect using a supported mechanism
3. detect hardware/firmware when exposed
4. read current configuration
5. calculate changes
6. show preview
7. obtain user confirmation
8. write configuration
9. read configuration back
10. verify
11. mark READY only on successful verification

## Security defaults

`copyDeviceIdentity=false`  
`copyPrivateKeys=false`

Gateway credentials and other production secrets stay external to `.ftak`.

## Ownership

The Hub must not fight the Meshtastic application for exclusive BLE access. If another application owns the connection, the user should be asked to release it or the workflow should use another supported transport.
