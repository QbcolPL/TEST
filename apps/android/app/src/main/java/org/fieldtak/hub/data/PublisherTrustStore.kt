package org.fieldtak.hub.data
import android.content.Context
class PublisherTrustStore(context: Context) {
  private val p=context.getSharedPreferences("trusted_publishers",Context.MODE_PRIVATE)
  fun isTrusted(fp:String)=p.getBoolean(fp,false)
  fun trust(fp:String)=p.edit().putBoolean(fp,true).apply()
  fun forget(fp:String)=p.edit().remove(fp).apply()
}
