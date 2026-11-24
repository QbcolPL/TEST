package com.fieldtakhub.hub.data

import android.content.Context
import com.fieldtakhub.hub.core.FtakArchive
import com.fieldtakhub.hub.core.FtakParser
import com.fieldtakhub.hub.core.HubPackage
import com.fieldtakhub.hub.core.Json
import com.fieldtakhub.hub.core.JsonObj
import com.fieldtakhub.hub.core.PackageKind
import com.fieldtakhub.hub.core.SignatureInfo
import com.fieldtakhub.hub.core.SignatureState
import com.fieldtakhub.hub.core.safeName
import java.io.File
import java.io.InputStream

/** Version of an APK bundled in a package, read once at import time. */
data class ApkInfo(
    val entry: String,
    val packageName: String,
    val versionName: String,
    val versionCode: Long,
    val label: String
)

data class StoredPackage(
    val id: String,
    val dir: File,
    val pkg: HubPackage,
    val importedAt: Long,
    val sha256: String,
    val sizeBytes: Long,
    val source: String,
    val fileName: String?,
    val apks: List<ApkInfo>
) {
    val archive: File? get() = File(dir, ARCHIVE).takeIf { it.isFile }

    fun apkFor(entry: String?): ApkInfo? = entry?.let { e -> apks.firstOrNull { it.entry == e } }

    companion object {
        const val ARCHIVE = "package.ftak"
        const val CONTENT = "content.json"
        const val META = "entry.json"
    }
}

/**
 * Packages live in filesDir/packages/<id>/: the original .ftak (or JSON for QR/OPORD imports)
 * plus entry.json with what was learned at import time (hash, signature result, APK versions).
 * One entry per Builder package id: importing a newer build replaces the older one.
 */
class PackageStore(context: Context) {
    private val root = File(context.filesDir, "packages").apply { mkdirs() }

    fun list(): List<StoredPackage> =
        root.listFiles()?.filter { it.isDirectory }?.mapNotNull { load(it) }?.sortedByDescending { it.importedAt }.orEmpty()

    fun idFor(pkg: HubPackage): String {
        val prefix = if (pkg.kind == PackageKind.OPORD_ONLY) "" else pkg.kind.name.lowercase() + "-"
        return safeName(prefix + pkg.packageId, "paczka").lowercase()
    }

    fun storeArchive(
        file: File,
        pkg: HubPackage,
        sha256: String,
        source: String,
        fileName: String?,
        apks: List<ApkInfo>
    ): StoredPackage {
        val id = idFor(pkg)
        val dir = File(root, id)
        dir.deleteRecursively()
        dir.mkdirs()
        val target = File(dir, StoredPackage.ARCHIVE)
        if (!file.renameTo(target)) {
            file.inputStream().use { input -> target.outputStream().use { input.copyTo(it) } }
            file.delete()
        }
        val stored = StoredPackage(id, dir, pkg, System.currentTimeMillis(), sha256, target.length(), source, fileName, apks)
        writeMeta(stored)
        return stored
    }

    fun storeJson(text: String, pkg: HubPackage, sha256: String, source: String): StoredPackage {
        val id = idFor(pkg)
        val dir = File(root, id)
        dir.deleteRecursively()
        dir.mkdirs()
        val content = File(dir, StoredPackage.CONTENT)
        content.writeText(text, Charsets.UTF_8)
        val stored = StoredPackage(id, dir, pkg, System.currentTimeMillis(), sha256, content.length(), source, null, emptyList())
        writeMeta(stored)
        return stored
    }

    fun delete(id: String) {
        File(root, safeName(id)).deleteRecursively()
    }

    fun openEntry(stored: StoredPackage, entry: String): InputStream? {
        val file = stored.archive ?: return null
        val archive = FtakArchive(file)
        val stream = archive.open(entry)
        if (stream == null) {
            archive.close()
            return null
        }
        return object : java.io.FilterInputStream(stream) {
            override fun close() {
                try { super.close() } finally { archive.close() }
            }
        }
    }

    fun extractEntry(stored: StoredPackage, entry: String, dest: File): File? {
        val input = openEntry(stored, entry) ?: return null
        dest.parentFile?.mkdirs()
        input.use { i -> dest.outputStream().use { i.copyTo(it, 256 * 1024) } }
        return dest
    }

    fun readText(stored: StoredPackage, entry: String): String? = stored.archive?.let { f ->
        FtakArchive(f).use { it.text(entry) }
    }

    private fun writeMeta(s: StoredPackage) {
        val map = linkedMapOf<String, Any?>(
            "id" to s.id,
            "kind" to s.pkg.kind.name,
            "packageId" to s.pkg.packageId,
            "title" to s.pkg.title,
            "importedAt" to s.importedAt,
            "sha256" to s.sha256,
            "sizeBytes" to s.sizeBytes,
            "source" to s.source,
            "fileName" to s.fileName,
            "signatureState" to s.pkg.signature.state.name,
            "signatureFingerprint" to s.pkg.signature.fingerprint,
            "signatureDetail" to s.pkg.signature.detail,
            "apks" to s.apks.map {
                linkedMapOf(
                    "entry" to it.entry, "packageName" to it.packageName, "versionName" to it.versionName,
                    "versionCode" to it.versionCode, "label" to it.label
                )
            }
        )
        File(s.dir, StoredPackage.META).writeText(Json.write(map, indent = true), Charsets.UTF_8)
    }

    private fun load(dir: File): StoredPackage? = try {
        val meta = Json.parseObject(File(dir, StoredPackage.META).readText(Charsets.UTF_8))
        val archive = File(dir, StoredPackage.ARCHIVE)
        val content = File(dir, StoredPackage.CONTENT)
        val parsed = when {
            archive.isFile -> FtakParser.inspect(archive, verify = false)
            content.isFile -> FtakParser.fromJsonText(content.readText(Charsets.UTF_8))
            else -> null
        }
        parsed?.let { pkg ->
            val state = runCatching { SignatureState.valueOf(meta.text("signatureState")) }.getOrDefault(pkg.signature.state)
            val signature = if (pkg.kind == PackageKind.FTAK_V2) SignatureInfo(
                state, meta.str("signatureFingerprint"), meta.text("signatureDetail").ifBlank { pkg.signature.detail }
            ) else pkg.signature
            StoredPackage(
                id = dir.name,
                dir = dir,
                pkg = pkg.copy(signature = signature),
                importedAt = meta.long("importedAt") ?: dir.lastModified(),
                sha256 = meta.text("sha256"),
                sizeBytes = meta.long("sizeBytes") ?: 0L,
                source = meta.text("source"),
                fileName = meta.str("fileName"),
                apks = meta.objects("apks").map { apk(it) }
            )
        }
    } catch (e: Exception) {
        null
    }

    private fun apk(o: JsonObj) = ApkInfo(
        entry = o.text("entry"),
        packageName = o.text("packageName"),
        versionName = o.text("versionName"),
        versionCode = o.long("versionCode") ?: 0L,
        label = o.text("label")
    )
}
