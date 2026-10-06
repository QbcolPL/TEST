# Android build audit — 2026-09-22

## Result

Static source audit: PASS.

The previously reported Kotlin blockers are fixed in the current source:
- `MainActivity.kt`: `onPush` is now a parameter of `FieldTakScreenContent` and is passed from `FieldTakApp`.
- `MainViewModel.kt`: `update()` accepts `updateMessage`.
- `MeshtasticLinkState` is defined and imported.
- `TargetInfo` is defined and imported.
- `Intent` is imported where used.
- Meshtastic broadcast registration/unregistration is implemented.

All repository Python contract tests pass after refreshing three stale test expectations/syntax issues.

A full Gradle build could not be executed in this environment because the repository archive does not contain `gradle/wrapper/gradle-wrapper.jar`; invoking `./gradlew assembleDebug` therefore stops before Gradle starts with `ClassNotFoundException: org.gradle.wrapper.GradleWrapperMain`.

## Builder UX

- New projects leave Meshtastic Channel URL empty.
- New projects leave ATAK enrollment URI empty.
- Existing `source/meshtastic/channel.url` and `source/enrollment/enroll.url` are still loaded when present.
- All non-editable and editable `DarkCombo` controls open their dropdown when clicking anywhere in the field; the native arrow remains functional and is not double-toggled.
