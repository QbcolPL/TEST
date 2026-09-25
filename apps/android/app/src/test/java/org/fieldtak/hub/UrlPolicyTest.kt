package org.fieldtak.hub

import org.fieldtak.hub.security.UrlPolicy
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class UrlPolicyTest {
  @Test fun httpsIsAccepted(){ assertTrue(UrlPolicy.requireProvisioningUrl("https://example.org/a").startsWith("https://")) }
  @Test fun publicHttpIsRejected(){ try { UrlPolicy.requireProvisioningUrl("http://8.8.8.8/a"); fail("expected rejection") } catch(_:IllegalArgumentException){} }
  @Test fun privateHttpIsAccepted(){ assertTrue(UrlPolicy.requireProvisioningUrl("http://192.168.10.2:8765/a").startsWith("http://")) }
}
