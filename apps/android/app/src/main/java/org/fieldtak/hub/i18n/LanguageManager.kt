package org.fieldtak.hub.i18n

import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat

object LanguageManager {
  fun apply(tag:String) {
    val locales = if(tag == "system") LocaleListCompat.getEmptyLocaleList() else LocaleListCompat.forLanguageTags(tag)
    AppCompatDelegate.setApplicationLocales(locales)
  }

  fun currentTag():String = AppCompatDelegate.getApplicationLocales().toLanguageTags().ifBlank { "system" }
}
