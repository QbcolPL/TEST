# Status Model 2.4

| Status | Meaning |
|---|---|
| READY | Verified as working for this deployment |
| NOT READY | Required state is not satisfied |
| WARNING | Usable but requires attention |
| UNKNOWN | Cannot be reliably inspected |
| NOT APPLICABLE | Outside selected topology/profile |
| OPTIONAL | Not required by this deployment |
| REQUIRED | Declared required by deployment policy |

Example: a field node should not fail because an infrastructure gateway is not locally connected. In that topology the local gateway state can be `NOT APPLICABLE` while the actual end-to-end path remains separately verifiable.
