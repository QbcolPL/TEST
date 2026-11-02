package org.fieldtak.hub.storage

import android.content.Context
import java.io.File
import java.time.Duration
import java.time.Instant

class StorageMaintenance(private val context:Context) {
  fun totalBytes():Long = listOf("downloads","imports","packages","updates").sumOf { dirSize(File(context.filesDir,it)) }

  fun cleanup(retainedPaths:Set<String> = emptySet()):Long {
    val before=totalBytes(); val now=System.currentTimeMillis()
    File(context.filesDir,"downloads").listFiles()?.forEach { f -> if(f.name.endsWith(".part") && now-f.lastModified()>Duration.ofDays(7).toMillis()) f.delete() }
    File(context.filesDir,"updates").listFiles()?.forEach { f -> if(now-f.lastModified()>Duration.ofDays(14).toMillis()) f.delete() }
    File(context.filesDir,"imports").listFiles()?.sortedByDescending{it.lastModified()}?.drop(3)?.forEach { f -> if(f.absolutePath !in retainedPaths) f.deleteRecursively() }
    File(context.filesDir,"packages").listFiles()?.sortedByDescending{it.lastModified()}?.drop(3)?.forEach { f -> if(f.absolutePath !in retainedPaths) f.deleteRecursively() }
    return (before-totalBytes()).coerceAtLeast(0)
  }

  private fun dirSize(file:File):Long = if(!file.exists())0 else if(file.isFile)file.length() else file.listFiles()?.sumOf(::dirSize) ?: 0
}
