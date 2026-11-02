# Field TAK Hub 2.3.0 Builder FIX8

## Build fixes
- Removed obsolete navigation field references (`NavServerNameBox`, `NavHostBox`, `NavCotBox`, `NavApiBox`, `NavWebBox`, `NavAtakMinBox`, `NavAtakMaxBox`).
- Left navigation now targets the canonical Advanced Mode controls directly.
- Fixed XAML event handler names for server.txt actions:
  - `LoadServer_Click` -> `LoadServerTxt_Click`
  - `SaveServer_Click` -> `SaveServerTxt_Click`

## Validation
- BUILDER ADVANCED COMPACT FHD CONTRACT: PASS
- BUILDER FULL-APP SCROLL CONTRACT: PASS
- BUILDER UI 2.4 FIX6 CONTRACT: PASS
- FULL STATIC GATE: PASS
