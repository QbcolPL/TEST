package org.fieldtak.hub.data

import android.content.Context
import android.net.Uri
import org.bouncycastle.crypto.params.Ed25519PublicKeyParameters
import org.bouncycastle.crypto.signers.Ed25519Signer
import org.fieldtak.hub.model.VerifiedPackage
import org.fieldtak.hub.security.UrlPolicy
import java.io.File
import java.io.RandomAccessFile
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.Base64
import java.util.zip.ZipInputStream

class PackageRepository(private val context: Context) {
  fun fetchDescriptor(url:String):String = connection(url).useConnection { c ->
    check(c.responseCode in 200..299) { "Descriptor HTTP ${c.responseCode}" }
    c.inputStream.bufferedReader().use { it.readText() }
  }

  /**
   * Resumable HTTP download. An existing .part file is continued with Range when the server supports 206.
   * If the origin ignores Range and replies 200, the partial file is safely replaced.
   */
  fun downloadResumable(url:String, key:String, expectedSha256:String?=null, progress:(Long,Long?)->Unit = {_,_->}):File {
    val dir=File(context.filesDir,"downloads").apply{mkdirs()}
    val part=File(dir,"$key.part"); val done=File(dir,"$key.ftak")
    if(done.isFile && (expectedSha256.isNullOrBlank() || sha256File(done).equals(expectedSha256,true))) return done
    val existing=part.takeIf{it.isFile}?.length() ?: 0L
    val c=connection(url).apply { if(existing>0) setRequestProperty("Range","bytes=$existing-") }
    return c.useConnection { conn ->
      val code=conn.responseCode
      check(code==HttpURLConnection.HTTP_OK || code==HttpURLConnection.HTTP_PARTIAL) { "Package HTTP $code" }
      val append=existing>0 && code==HttpURLConnection.HTTP_PARTIAL
      val start=if(append) existing else 0L
      val responseLength=conn.getHeaderFieldLong("Content-Length",-1L).takeIf{it>=0}
      val total=when { responseLength==null->null; append->start+responseLength; else->responseLength }
      RandomAccessFile(part,"rw").use { raf ->
        if(append) raf.seek(start) else raf.setLength(0)
        conn.inputStream.use { input ->
          val buffer=ByteArray(128*1024); var current=start
          while(true){ val n=input.read(buffer); if(n<0) break; raf.write(buffer,0,n); current+=n; progress(current,total) }
        }
      }
      if(!expectedSha256.isNullOrBlank()) require(sha256File(part).equals(expectedSha256,true)){"Downloaded package SHA-256 mismatch"}
      if(done.exists()) done.delete(); require(part.renameTo(done)){"Cannot finalize downloaded package"}; done
    }
  }

  fun copyFromUri(uri:Uri):File {
    val dir=File(context.filesDir,"imports").apply{mkdirs()}
    val f=File(dir,"import-${System.currentTimeMillis()}.ftak")
    context.contentResolver.openInputStream(uri)!!.use { input -> f.outputStream().use { output -> input.copyTo(output) } }
    return f
  }

  fun verify(ftak:File):VerifiedPackage {
    require(ftak.isFile){"Package file missing"}
    val root=File(context.filesDir,"packages/${System.currentTimeMillis()}").apply{mkdirs()}
    var extractedBytes=0L
    ZipInputStream(ftak.inputStream().buffered()).use { zis ->
      while(true){
        val e=zis.nextEntry ?: break
        val dst=File(root,e.name).canonicalFile
        require(dst.path.startsWith(root.canonicalPath+File.separator)){"Unsafe zip entry"}
        if(e.isDirectory) dst.mkdirs() else {
          dst.parentFile?.mkdirs()
          dst.outputStream().use{out->
            val buf=ByteArray(128*1024)
            while(true){ val n=zis.read(buf); if(n<0) break; extractedBytes+=n; require(extractedBytes<=12L*1024*1024*1024){"Package extracted size exceeds 12 GiB safety limit"}; out.write(buf,0,n) }
          }
        }
      }
    }
    val meta=File(root,"META-INF")
    val manifestFile=File(meta,"fieldtak.json"); val checks=File(meta,"checksums.sha256"); val sig=File(meta,"signature.ed25519"); val pub=File(meta,"publisher.pub")
    require(manifestFile.isFile && checks.isFile && sig.isFile && pub.isFile){"Incomplete .ftak META-INF"}
    val pubBytes=Base64.getDecoder().decode(pub.readText().trim()); require(pubBytes.size==32){"Invalid Ed25519 public key"}
    val signature=Base64.getDecoder().decode(sig.readText().trim()); require(signature.size==64){"Invalid Ed25519 signature"}
    val signer=Ed25519Signer(); signer.init(false,Ed25519PublicKeyParameters(pubBytes,0)); val checkBytes=checks.readBytes(); signer.update(checkBytes,0,checkBytes.size); val sigOk=signer.verifySignature(signature)
    var hashesOk=true
    val listed = linkedSetOf<String>()
    checks.readLines().filter{it.isNotBlank()}.forEach { line ->
      val parts=line.split("  ",limit=2)
      if(parts.size!=2) { hashesOk=false; return@forEach }
      val rel=parts[1].replace('\\','/')
      listed += rel
      val f=File(root,rel).canonicalFile
      if(!f.path.startsWith(root.canonicalPath+File.separator) || !f.isFile) { hashesOk=false; return@forEach }
      val actual=sha256File(f); if(!actual.equals(parts[0],true)) hashesOk=false
    }
    val unsignedMeta = setOf("META-INF/checksums.sha256","META-INF/signature.ed25519","META-INF/publisher.pub")
    val actualSignedFiles = root.walkTopDown().filter { it.isFile }.map { it.relativeTo(root).invariantSeparatorsPath }
      .filter { it !in unsignedMeta }.toSet()
    if(actualSignedFiles != listed.toSet()) hashesOk=false
    val fp=sha256(pubBytes)
    val m=SimpleJson.manifest(manifestFile.readText())
    if(!m.security.publisherFingerprintSha256.equals(fp,true)) hashesOk=false
    return VerifiedPackage(root,ftak,m,fp,sigOk,hashesOk)
  }

  fun sha256File(file:File):String {
    val md=MessageDigest.getInstance("SHA-256"); file.inputStream().buffered().use { input -> val b=ByteArray(128*1024); while(true){val n=input.read(b);if(n<0)break;md.update(b,0,n)} }
    return md.digest().joinToString(""){"%02x".format(it)}
  }
  private fun connection(initial:String):HttpURLConnection {
    var current=UrlPolicy.requireProvisioningUrl(initial)
    repeat(6) {
      val c=(URL(current).openConnection() as HttpURLConnection).apply {
        connectTimeout=15_000; readTimeout=60_000; instanceFollowRedirects=false; requestMethod="GET"
        setRequestProperty("User-Agent","FieldTAKHub/${org.fieldtak.hub.BuildConfig.VERSION_NAME}")
      }
      val code=c.responseCode
      if(code in 300..399){
        val location=c.getHeaderField("Location") ?: error("FTH-DL-007: redirect without Location")
        val next=URL(URL(current),location).toString(); c.disconnect(); current=UrlPolicy.requireProvisioningUrl(next)
      } else return c
    }
    error("FTH-DL-008: too many redirects")
  }
  private inline fun <T> HttpURLConnection.useConnection(block:(HttpURLConnection)->T):T = try { block(this) } finally { disconnect() }
  private fun sha256(b:ByteArray)=MessageDigest.getInstance("SHA-256").digest(b).joinToString(""){"%02x".format(it)}
}
