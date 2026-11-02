package org.fieldtak.hub.util

object VersionUtil {
  fun compare(a:String?, b:String?):Int {
    if(a.isNullOrBlank() && b.isNullOrBlank()) return 0
    if(a.isNullOrBlank()) return -1
    if(b.isNullOrBlank()) return 1
    val aa=parts(a); val bb=parts(b); val n=maxOf(aa.size,bb.size)
    for(i in 0 until n){
      val x=aa.getOrElse(i){0}; val y=bb.getOrElse(i){0}
      if(x!=y) return x.compareTo(y)
    }
    return 0
  }

  fun inRange(value:String?, min:String?, max:String?):Boolean {
    if(value.isNullOrBlank()) return false
    if(!min.isNullOrBlank() && compare(value,min)<0) return false
    if(max.isNullOrBlank()) return true
    if(value == max || value.startsWith(max + ".")) return true
    if(max.contains('x',true)) {
      val prefix=max.substringBefore('x',"").trimEnd('.')
      return prefix.isBlank() || value.startsWith(prefix)
    }
    return compare(value,max)<=0
  }

  private fun parts(v:String):List<Int> = Regex("\\d+").findAll(v).map { it.value.toIntOrNull() ?: 0 }.toList()
}
