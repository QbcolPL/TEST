# Field TAK Hub Builder — UI 2.4 BETA design implementation

Builder 2.3.0 uses the 2.4 visual layout as the design baseline while keeping the 2.3 build engine and package format.

## Visual reference implemented

- 340 px navigation sidebar
- 108 px top bar
- dark tactical background with cyan accent
- Field TAK Hub logo and connect/deploy/operate identity
- three-column workspace: navigation / package wizard / package status
- Simple / Advanced mode switch
- event information section with profile + package/QR validity
- six package content tiles
- recommended profile shortcut
- per-tile Configure action which reveals Advanced mode
- package status, contents preview, generated file and QR panels
- fixed bottom action bar with clear and generate actions
- custom dark ComboBox instead of native white WPF selector rendering
- custom cyan toggle switches instead of native Windows CheckBox rendering
- immediate PL / EN language switch in the top bar

## Readability changes

The 2.4 UI uses larger headings, stronger contrast between input/panel/background layers, wider fields, consistent card padding and darker control templates. Native white ComboBox backgrounds are removed from the primary workflow.

## Functional continuity

All existing 2.3 backend and advanced-flow controls remain available to the application engine, including source analysis, package build/signing, OpenTAKServer, enrollment, Meshtastic HYBRID, gateway MQTT/TLS, QR/cloud distribution and legacy import.
