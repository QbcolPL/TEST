# Android build audit — Field TAK Hub 2.3.0

## Verified against the latest local source

The Android source was statically audited after the reported `:app:compileDebugKotlin` failure.

### Known compiler errors corrected

1. `MainActivity.kt` — `HomeScreen` referenced `onPush` out of scope. The diagnostic-log action now receives an explicit `onLogs` callback from `FieldTakScreenContent`.
2. `MainViewModel.kt` — `update(session=..., updateMessage=...)` was used by the optional-step skip paths while `update()` did not declare `updateMessage`. The parameter was added and preserved by default.
3. All other `R.string.*` references resolve against the Android string resources (including the Polish and default resources).
4. Drawable references resolve against the packaged Android resources.

## Build environment limitation

This container does not contain a Gradle wrapper JAR or a system Gradle installation, so a real `assembleDebug` could not be executed here. The user's Android Studio output is therefore the authoritative compile test.

The message about native libraries being "Unable to strip" is a packaging warning, not a Kotlin compilation error.

## Builder change included in this package

The Meshtastic Channel URL field is intentionally blank for new projects. Existing `source/meshtastic/channel.url` files are still loaded when present.

All styled `ComboBox` controls now open their drop-down when the user clicks the field itself, not only the arrow. Editable text ComboBoxes remain editable and keep their normal text-entry behavior.
