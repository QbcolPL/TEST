import java.util.Properties

plugins {
  id("com.android.application")
  id("org.jetbrains.kotlin.plugin.compose")
}

android {
  namespace = "org.fieldtak.hub"
  compileSdk = 37

  defaultConfig {
    applicationId = "org.fieldtak.hub"
    minSdk = 26
    targetSdk = 37
    versionCode = 23005
    versionName = "2.3.5"
    buildConfigField("String", "UPDATE_REPOSITORY", "\"Qbcol/TAKFieldHub\"")
    buildConfigField("String", "RELEASE_CHANNEL", "\"rc\"")
  }

  buildFeatures { compose = true; buildConfig = true }

  compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }

  val ksFile = rootProject.file("keystore.properties")
  if (ksFile.exists()) {
    val p = Properties().apply { ksFile.inputStream().use(::load) }
    signingConfigs {
      create("release") {
        storeFile = file(p.getProperty("storeFile")); storePassword = p.getProperty("storePassword")
        keyAlias = p.getProperty("keyAlias"); keyPassword = p.getProperty("keyPassword")
      }
    }
    buildTypes.getByName("release").signingConfig = signingConfigs.getByName("release")
  }

  buildTypes {
    release { isMinifyEnabled = false; proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro") }
  }
  packaging { resources.excludes += setOf("META-INF/DEPENDENCIES", "META-INF/LICENSE*", "META-INF/NOTICE*") }
}

dependencies {
    implementation("com.google.zxing:core:3.5.3")
  val composeBom = platform("androidx.compose:compose-bom:2026.08.00")
  implementation(composeBom)

  implementation("androidx.core:core-ktx:1.18.0")
  implementation("androidx.appcompat:appcompat:1.7.1")
  implementation("androidx.activity:activity-compose:1.13.0")
  implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.11.0")
  implementation("androidx.lifecycle:lifecycle-runtime-compose:2.11.0")
  implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.11.0")
  implementation("androidx.compose.ui:ui")
  implementation("androidx.compose.ui:ui-tooling-preview")
  implementation("androidx.compose.material3:material3")
  implementation("androidx.camera:camera-core:1.6.0")
  implementation("androidx.camera:camera-camera2:1.6.0")
  implementation("androidx.camera:camera-lifecycle:1.6.0")
  implementation("androidx.camera:camera-view:1.6.0")
  implementation("com.google.mlkit:barcode-scanning:17.3.0")
  implementation("org.bouncycastle:bcprov-jdk18on:1.85.2")
  debugImplementation("androidx.compose.ui:ui-tooling")
  testImplementation("junit:junit:4.13.2")
}
