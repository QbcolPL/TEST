package org.fieldtak.hub.deployment

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

class DeploymentStore(context:Context) {
  private val prefs = context.getSharedPreferences("fieldtak.deployments.v2", Context.MODE_PRIVATE)
  private val currentSchema = 1

  init { migrateIfNeeded() }

  private fun migrateIfNeeded() {
    val old=prefs.getInt("storageSchemaVersion",0)
    if(old<1) prefs.edit().putInt("storageSchemaVersion",1).apply()
    require(prefs.getInt("storageSchemaVersion",1)<=currentSchema) { "FTH-STO-001: storage schema is newer than this app" }
  }

  fun active():DeploymentSession? = prefs.getString("active", null)?.let(::decode)

  fun saveActive(session:DeploymentSession) {
    prefs.edit().putString("active", encode(session).toString()).apply()
  }

  fun clearActive() { prefs.edit().remove("active").apply() }

  fun history(limit:Int = 20):List<DeploymentSession> {
    val a = JSONArray(prefs.getString("history", "[]"))
    return buildList {
      for (i in 0 until minOf(a.length(), limit)) runCatching { add(decode(a.getJSONObject(i).toString())) }
    }
  }

  fun archive(session:DeploymentSession) {
    val current = history(50).filterNot { it.id == session.id }.toMutableList()
    current.add(0, session)
    val arr = JSONArray(); current.take(20).forEach { arr.put(encode(it)) }
    prefs.edit().putString("history", arr.toString()).remove("active").apply()
  }

  fun clearHistory() { prefs.edit().remove("history").apply() }

  fun retainedPaths(historyLimit:Int=3):Set<String> = buildSet {
    active()?.let { s -> listOfNotNull(s.localPackagePath,s.extractedRoot).forEach(::add) }
    history(historyLimit).forEach { s -> listOfNotNull(s.localPackagePath,s.extractedRoot).forEach(::add) }
  }

  private fun encode(s:DeploymentSession)=JSONObject().apply {
    put("id",s.id); put("packageId",s.packageId); put("packageName",s.packageName); put("packageVersion",s.packageVersion)
    put("sourceDescriptor",s.sourceDescriptor); put("packageUrl",s.packageUrl); put("localPackagePath",s.localPackagePath); put("extractedRoot",s.extractedRoot)
    put("stage",s.stage.name); put("startedUtc",s.startedUtc); put("updatedUtc",s.updatedUtc); put("completedUtc",s.completedUtc)
    put("steps",JSONArray().apply { s.steps.forEach { step -> put(JSONObject().apply { put("stage",step.stage.name); put("result",step.result.name); put("detail",step.detail); put("updatedUtc",step.updatedUtc) }) } })
  }

  private fun decode(raw:String):DeploymentSession {
    val o=JSONObject(raw); val steps=o.optJSONArray("steps") ?: JSONArray()
    return DeploymentSession(
      id=o.getString("id"), packageId=o.optString("packageId"), packageName=o.optString("packageName"), packageVersion=o.optString("packageVersion"),
      sourceDescriptor=o.optNullable("sourceDescriptor"), packageUrl=o.optNullable("packageUrl"), localPackagePath=o.optNullable("localPackagePath"), extractedRoot=o.optNullable("extractedRoot"),
      stage=runCatching{DeploymentStage.valueOf(o.getString("stage"))}.getOrDefault(DeploymentStage.NEW), startedUtc=o.optString("startedUtc"), updatedUtc=o.optString("updatedUtc"), completedUtc=o.optNullable("completedUtc"),
      steps=buildList { for(i in 0 until steps.length()){ val x=steps.getJSONObject(i); add(DeploymentStep(runCatching{DeploymentStage.valueOf(x.getString("stage"))}.getOrDefault(DeploymentStage.NEW),runCatching{StepResult.valueOf(x.getString("result"))}.getOrDefault(StepResult.PENDING),x.optString("detail"),x.optString("updatedUtc"))) } }
    )
  }

  private fun JSONObject.optNullable(key:String):String? = if(isNull(key) || !has(key)) null else optString(key).ifBlank { null }
}
