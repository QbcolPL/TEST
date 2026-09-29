package org.fieldtak.hub

import org.fieldtak.hub.util.VersionUtil
import org.junit.Assert.*
import org.junit.Test

class VersionUtilTest {
  @Test fun compareVersions(){
    assertTrue(VersionUtil.compare("5.8.1","5.8")>0)
    assertEquals(0,VersionUtil.compare("5.8","5.8.0"))
    assertTrue(VersionUtil.compare("5.7.9","5.8")<0)
  }
  @Test fun range(){
    assertTrue(VersionUtil.inRange("5.8.1","5.6","5.8"))
    assertFalse(VersionUtil.inRange("5.5.9","5.6","5.8.9"))
  }
}
