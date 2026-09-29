# FTAK v2 — 2.4 DATA CONTRACT

This document defines proposed additions to the existing FTAK Schema v2 without changing the schema major version.

## Deployment

```json
{
  "deployment": {
    "atakRequired": true,
    "pluginsRequired": true,
    "serverRequired": true,
    "meshtasticRequired": false,
    "enrollmentRequired": false,
    "missionPackageRequired": false,
    "mapsRequired": false,
    "overlaysRequired": false
  }
}
```

## Meshtastic 2.4 target

```json
{
  "meshtastic": {
    "enabled": true,
    "profileVersion": 1,
    "mode": "FIELD_NODE",
    "universalProfile": "field-node",
    "hardwareOverrides": {
      "TBEAM": "tbeam-field",
      "TBEAM_SUPREME": "tbeam-supreme-field",
      "MESH_TRACKER_X1": "x1-field"
    },
    "security": {
      "copyDeviceIdentity": false,
      "copyPrivateKeys": false
    }
  }
}
```

The exact field names remain subject to implementation against the supported Meshtastic protocol/SDK version. Do not treat this proposal as a claim that every field is already implemented.
