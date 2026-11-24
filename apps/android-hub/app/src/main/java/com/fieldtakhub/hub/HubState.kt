package com.fieldtakhub.hub

import android.content.Intent
import com.fieldtakhub.hub.core.AppRequirement
import com.fieldtakhub.hub.core.QrPayload
import com.fieldtakhub.hub.core.WifiCredentials
import com.fieldtakhub.hub.data.ApkInfo
import com.fieldtakhub.hub.data.StoredPackage
import com.fieldtakhub.hub.platform.InstalledApp

enum class Tab { SCANNER, PACKAGES, OPORD, CHEATSHEET }

enum class CheckState { OK, MISSING, OUTDATED }

data class AppCheck(
    val requirement: AppRequirement,
    val apk: ApkInfo?,
    val installed: InstalledApp?,
    val state: CheckState,
    val name: String,
    val detail: String
) {
    val packageName: String? get() = apk?.packageName ?: installed?.packageName ?: requirement.packageName
}

enum class ServerState { NONE, CHECKING, REACHABLE, UNREACHABLE }

data class Busy(val title: String, val detail: String = "", val progress: Float? = null, val cancellable: Boolean = true)

data class NoticeAction(val label: String, val primary: Boolean = false, val run: () -> Unit)

data class Notice(
    val title: String,
    val text: String,
    val error: Boolean = false,
    val actions: List<NoticeAction> = emptyList()
)

enum class SharePhase { STARTING, READY, FAILED }

data class ShareState(
    val entryId: String,
    val title: String,
    val phase: SharePhase,
    val viaHotspot: Boolean = true,
    val qrText: String? = null,
    val ssid: String? = null,
    val password: String? = null,
    val url: String? = null,
    val token: String? = null,
    val downloads: Int = 0,
    val error: String? = null
)

data class WifiPrompt(val credentials: WifiCredentials, val payload: QrPayload.Download)

data class HubState(
    val loaded: Boolean = false,
    val onboardingDone: Boolean = false,
    val tab: Tab = Tab.SCANNER,
    val packages: List<StoredPackage> = emptyList(),
    val activeId: String? = null,
    val opordId: String? = null,
    val callsign: String = "",
    val shortName: String = "",
    val shortManual: Boolean = false,
    val apps: List<AppCheck> = emptyList(),
    val server: ServerState = ServerState.NONE,
    val busy: Busy? = null,
    val notice: Notice? = null,
    val injectOpen: Boolean = false,
    val injectDone: Set<String> = emptySet(),
    val share: ShareState? = null,
    val wifiPrompt: WifiPrompt? = null,
    val detailsId: String? = null,
    val documentOpen: Boolean = false,
    val showPermissions: Boolean = false
) {
    val active: StoredPackage? get() = packages.firstOrNull { it.id == activeId && it.pkg.hasDeployment }
    val opordSource: StoredPackage? get() = packages.firstOrNull { it.id == opordId && it.pkg.hasOpord }
    val details: StoredPackage? get() = packages.firstOrNull { it.id == detailsId }
    val overlayOpen: Boolean
        get() = busy != null || notice != null || injectOpen || share != null || wifiPrompt != null || detailsId != null || documentOpen

    /** Organiser phone: active package first, then the package the OPORD comes from. */
    val emergencyPhone: String?
        get() = active?.pkg?.emergencyPhone ?: opordSource?.pkg?.emergencyPhone ?: packages.firstNotNullOfOrNull { it.pkg.emergencyPhone }
}

sealed class UiCommand {
    data class Launch(val intent: Intent, val fallback: Intent? = null, val failMessage: String? = null) : UiCommand()
}
