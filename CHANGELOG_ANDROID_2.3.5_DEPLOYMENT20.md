# Field TAK Hub Android 2.3.5 — Deployment 2.0 (development)

## Cel

Development release focused on the original one-QR deployment workflow: **SCAN → VERIFY → CHECK → DEPLOY → VERIFY**.

## Changes

- Added explicit `DeploymentPlan` and per-component deployment states.
- Added automatic Meshtastic channel hand-off after Meshtastic app readiness.
- Deployment resumes after returning from Android package installer, ATAK, or Meshtastic.
- Optional ATAK data/map package is no longer treated as a hard blocker.
- OpenTAK enrollment is tracked as a deployment step instead of a separate manual workflow.
- Final completion is gated by required deployment tasks and OTS network verification.
- Added Deployment 2.0 checklist to the Simple status screen.
- Version: Android 2.3.5 / versionCode 23005 / development channel.

## Platform limitations kept explicit

- Android package installation may require system confirmation and `REQUEST_INSTALL_PACKAGES`; the app cannot silently bypass Android installer controls.
- ATAK enrollment and Mission Package are handed to ATAK; stock Android does not expose authenticated ATAK state to Field TAK Hub.
- Meshtastic channel configuration is handed to the official Meshtastic app; Field TAK Hub does not directly control the radio.
