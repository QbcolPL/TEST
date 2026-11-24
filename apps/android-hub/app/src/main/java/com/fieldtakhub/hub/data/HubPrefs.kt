package com.fieldtakhub.hub.data

import android.content.Context

/** Small user settings. Package contents live in [PackageStore]. */
class HubPrefs(context: Context) {
    private val sp = context.getSharedPreferences("field_tak_hub", Context.MODE_PRIVATE)

    var onboardingDone: Boolean
        get() = sp.getBoolean("onboarding_done", false)
        set(v) = sp.edit().putBoolean("onboarding_done", v).apply()

    var callsign: String
        get() = sp.getString("callsign", "").orEmpty()
        set(v) = sp.edit().putString("callsign", v).apply()

    var shortName: String
        get() = sp.getString("short_name", "").orEmpty()
        set(v) = sp.edit().putString("short_name", v).apply()

    /** True when the user typed the short name; then it is no longer regenerated from the callsign. */
    var shortManual: Boolean
        get() = sp.getBoolean("short_manual", false)
        set(v) = sp.edit().putBoolean("short_manual", v).apply()

    var activeId: String?
        get() = sp.getString("active_id", null)
        set(v) = sp.edit().putString("active_id", v).apply()

    var opordId: String?
        get() = sp.getString("opord_id", null)
        set(v) = sp.edit().putString("opord_id", v).apply()

    /** Publisher key fingerprint first seen for a Builder package id (trust on first use). */
    fun publisherFor(packageId: String): String? = sp.getString("publisher_$packageId", null)

    fun rememberPublisher(packageId: String, fingerprint: String) {
        sp.edit().putString("publisher_$packageId", fingerprint).apply()
    }
}
