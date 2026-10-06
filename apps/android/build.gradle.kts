buildscript {
  repositories { google(); mavenCentral(); gradlePluginPortal() }
  dependencies {
    // AGP 9 uses built-in Kotlin. Pin a newer KGP runtime so it matches the Compose compiler plugin.
    classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.3.21")
  }
}

plugins {
  id("com.android.application") version "9.4.0" apply false
  id("org.jetbrains.kotlin.plugin.compose") version "2.3.21" apply false
}
