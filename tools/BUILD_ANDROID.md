# Android build — Field TAK Hub 2.3.0

## Android Studio

1. Open `apps/android` in Android Studio.
2. Use JDK 17+.
3. Allow Gradle Sync to download Gradle 9.6 and Android/Kotlin dependencies.
4. Build → Make Project.
5. For APK: Gradle task `assembleDebug`.

The project is intentionally supplied as source. If the local checkout does not contain the Gradle wrapper JAR, Android Studio can recreate/use the configured Gradle distribution during sync.

## Release signing

Copy `keystore.properties.example` to `keystore.properties` and fill in private keystore values. Do not commit that file.

## Included Android behavior

- Simple Mode with optional-step **POMIŃ** actions.
- Meshtastic/LoRa HYBRID configuration.
- OpenTAKServer provisioning and diagnostics.
- Smart Diagnosis / Recovery.
- Back navigation: first Back returns to the previous screen; second Back within 2 seconds minimizes the app from the home screen.
