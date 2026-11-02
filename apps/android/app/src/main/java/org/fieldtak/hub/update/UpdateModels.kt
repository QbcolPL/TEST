package org.fieldtak.hub.update

data class AppUpdateInfo(
  val version:String,
  val versionCode:Long,
  val assetName:String,
  val assetUrl:String,
  val sha256:String,
  val releasePage:String,
  val channel:String
)
