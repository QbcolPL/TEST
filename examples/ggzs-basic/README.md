# GGZS example project

Non-secret example configuration for Field TAK Hub Builder.

Default server profile:

- name: `GGZS OpenTAK`
- host: `ggzstak.duckdns.org`
- CoT: `8089`
- API / enrollment: `8446`
- Web: `8443`

You can change these values directly in Builder 2.0.1. `server.txt` contains the same sample in a human-editable format and can be loaded into the GUI. When a package is built, Builder generates the ATAK `server.pref` from the active GUI/project values unless `source/atak/` already contains your own `.pref`.
