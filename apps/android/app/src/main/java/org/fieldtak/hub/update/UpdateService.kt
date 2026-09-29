package org.fieldtak.hub.update

import android.content.Context
import org.fieldtak.hub.BuildConfig
import org.fieldtak.hub.security.UrlPolicy
import org.fieldtak.hub.util.VersionUtil
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

class UpdateService(private val context:Context) {
  private val userAgent = "FieldTAKHub/${BuildConfig.VERSION_NAME}"

  fun check():AppUpdateInfo? {
    val repo=BuildConfig.UPDATE_REPOSITORY.trim()
    if(repo.isBlank() || !repo.contains('/')) return null
    val releases=JSONArray(getText("https://api.github.com/repos/$repo/releases?per_page=20"))
    var selected:JSONObject?=null
    for(i in 0 until releases.length()){
      val r=releases.getJSONObject(i)
      if(r.optBoolean("draft",false)) continue
      val prerelease=r.optBoolean("prerelease",false)
      if((BuildConfig.RELEASE_CHANNEL=="rc" && prerelease) || (BuildConfig.RELEASE_CHANNEL=="stable" && !prerelease)){ selected=r; break }
    }
    val release=selected ?: return null
    val assets=release.optJSONArray("assets") ?: return null
    var manifestUrl:String?=null
    val downloadByName=linkedMapOf<String,String>()
    for(i in 0 until assets.length()){
      val a=assets.getJSONObject(i); val name=a.optString("name"); val url=a.optString("browser_download_url")
      if(name.isNotBlank() && url.isNotBlank()) downloadByName[name]=url
      if(name=="fieldtak-release.json") manifestUrl=url
    }
    val manifest=JSONObject(getText(manifestUrl ?: return null))
    if(manifest.optString("channel") != BuildConfig.RELEASE_CHANNEL) return null
    val hub=manifest.optJSONObject("hub") ?: return null
    val version=hub.optString("version"); val versionCode=hub.optLong("versionCode",0)
    val asset=hub.optString("asset"); val sha=hub.optString("sha256").lowercase()
    if(version.isBlank() || asset.isBlank() || sha.length!=64) return null
    if(versionCode <= BuildConfig.VERSION_CODE && VersionUtil.compare(version,BuildConfig.VERSION_NAME)<=0) return null
    val assetUrl=downloadByName[asset] ?: return null
    return AppUpdateInfo(version,versionCode,asset,assetUrl,sha,release.optString("html_url"),manifest.optString("channel"))
  }

  fun download(update:AppUpdateInfo, progress:(Long,Long?)->Unit={_,_->}):File {
    val dir=File(context.filesDir,"updates").apply{mkdirs()}
    val part=File(dir,update.assetName+".part"); val done=File(dir,update.assetName)
    if(done.isFile && sha256(done).equals(update.sha256,true)) return done
    val conn=openHttps(update.assetUrl)
    try {
      require(conn.responseCode in 200..299){"FTH-UPD-003: HTTP ${conn.responseCode}"}
      val total=conn.getHeaderFieldLong("Content-Length",-1L).takeIf{it>=0}
      conn.inputStream.use { input -> part.outputStream().use { out ->
        val buf=ByteArray(128*1024); var current=0L
        while(true){val n=input.read(buf);if(n<0)break;out.write(buf,0,n);current+=n;progress(current,total)}
      }}
    } finally { conn.disconnect() }
    require(sha256(part).equals(update.sha256,true)){"FTH-UPD-004: downloaded APK SHA-256 mismatch"}
    if(done.exists()) done.delete(); require(part.renameTo(done)){"FTH-UPD-005: cannot finalize update"}; return done
  }

  private fun getText(url:String):String {
    val c=openHttps(url)
    return try { require(c.responseCode in 200..299){"FTH-UPD-006: HTTP ${c.responseCode}"}; c.inputStream.bufferedReader().use{it.readText()} }
    finally { c.disconnect() }
  }

  private fun openHttps(initial:String):HttpURLConnection {
    var current=UrlPolicy.requireHttps(initial)
    repeat(6) {
      val c=(URL(current).openConnection() as HttpURLConnection).apply {
        connectTimeout=12_000; readTimeout=30_000; instanceFollowRedirects=false; requestMethod="GET"
        setRequestProperty("User-Agent",userAgent); setRequestProperty("Accept","application/vnd.github+json")
      }
      val code=c.responseCode
      if(code in 300..399){ val next=c.getHeaderField("Location") ?: error("FTH-UPD-007: redirect without Location"); c.disconnect(); current=UrlPolicy.requireHttps(URL(URL(current),next).toString()) }
      else return c
    }
    error("FTH-UPD-008: too many redirects")
  }

  private fun sha256(file:File):String {
    val md=MessageDigest.getInstance("SHA-256")
    file.inputStream().buffered().use{input->val b=ByteArray(128*1024);while(true){val n=input.read(b);if(n<0)break;md.update(b,0,n)}}
    return md.digest().joinToString(""){"%02x".format(it)}
  }
}
