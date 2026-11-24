package com.fieldtakhub.hub.core

import java.math.BigInteger
import java.security.MessageDigest

/**
 * Ed25519 signature verification (RFC 8032, section 5.1.7).
 *
 * Builder signs META-INF/checksums.sha256 with NSec Ed25519. Android has Ed25519 in
 * java.security only from API 33, so the app carries this small, verification-only
 * implementation. It is not constant-time, which is fine: it only handles public data.
 */
object Ed25519 {
    private val P: BigInteger = BigInteger.ONE.shiftLeft(255).subtract(BigInteger.valueOf(19))
    private val L: BigInteger = BigInteger.ONE.shiftLeft(252).add(BigInteger("27742317777372353535851937790883648493"))
    private val TWO = BigInteger.valueOf(2)
    private val D: BigInteger = BigInteger.valueOf(-121665).multiply(BigInteger.valueOf(121666).modInverse(P)).mod(P)
    private val SQRT_M1: BigInteger = TWO.modPow(P.subtract(BigInteger.ONE).shiftRight(2), P)

    private class Point(val x: BigInteger, val y: BigInteger, val z: BigInteger, val t: BigInteger)

    private val NEUTRAL = Point(BigInteger.ZERO, BigInteger.ONE, BigInteger.ONE, BigInteger.ZERO)
    private val BASE: Point = run {
        val gy = BigInteger.valueOf(4).multiply(BigInteger.valueOf(5).modInverse(P)).mod(P)
        val gx = recoverX(gy, 0) ?: error("Ed25519 base point")
        Point(gx, gy, BigInteger.ONE, gx.multiply(gy).mod(P))
    }

    fun verify(publicKey: ByteArray, message: ByteArray, signature: ByteArray): Boolean {
        if (publicKey.size != 32 || signature.size != 64) return false
        val a = decompress(publicKey) ?: return false
        val rBytes = signature.copyOfRange(0, 32)
        val r = decompress(rBytes) ?: return false
        val s = leToInt(signature.copyOfRange(32, 64))
        if (s >= L) return false
        val sha = MessageDigest.getInstance("SHA-512")
        sha.update(rBytes)
        sha.update(publicKey)
        sha.update(message)
        val h = leToInt(sha.digest()).mod(L)
        val sB = mul(s, BASE)
        val hA = mul(h, a)
        return equal(sB, add(r, hA))
    }

    private fun leToInt(bytes: ByteArray): BigInteger = BigInteger(1, bytes.reversedArray())

    private fun recoverX(y: BigInteger, sign: Int): BigInteger? {
        if (y >= P) return null
        val y2 = y.multiply(y)
        val x2 = y2.subtract(BigInteger.ONE).multiply(D.multiply(y2).add(BigInteger.ONE).modInverse(P)).mod(P)
        if (x2.signum() == 0) return if (sign == 1) null else BigInteger.ZERO
        var x = x2.modPow(P.add(BigInteger.valueOf(3)).shiftRight(3), P)
        if (x.multiply(x).subtract(x2).mod(P).signum() != 0) x = x.multiply(SQRT_M1).mod(P)
        if (x.multiply(x).subtract(x2).mod(P).signum() != 0) return null
        if (x.testBit(0) != (sign == 1)) x = P.subtract(x)
        return x
    }

    private fun decompress(bytes: ByteArray): Point? {
        if (bytes.size != 32) return null
        val sign = (bytes[31].toInt() ushr 7) and 1
        val copy = bytes.copyOf()
        copy[31] = (copy[31].toInt() and 0x7F).toByte()
        val y = leToInt(copy)
        val x = recoverX(y, sign) ?: return null
        return Point(x, y, BigInteger.ONE, x.multiply(y).mod(P))
    }

    private fun add(p: Point, q: Point): Point {
        val a = p.y.subtract(p.x).multiply(q.y.subtract(q.x)).mod(P)
        val b = p.y.add(p.x).multiply(q.y.add(q.x)).mod(P)
        val c = TWO.multiply(p.t).multiply(q.t).multiply(D).mod(P)
        val d = TWO.multiply(p.z).multiply(q.z).mod(P)
        val e = b.subtract(a)
        val f = d.subtract(c)
        val g = d.add(c)
        val h = b.add(a)
        return Point(e.multiply(f).mod(P), g.multiply(h).mod(P), f.multiply(g).mod(P), e.multiply(h).mod(P))
    }

    private fun mul(scalar: BigInteger, point: Point): Point {
        var q = NEUTRAL
        var p = point
        var s = scalar
        while (s.signum() > 0) {
            if (s.testBit(0)) q = add(q, p)
            p = add(p, p)
            s = s.shiftRight(1)
        }
        return q
    }

    private fun equal(p: Point, q: Point): Boolean {
        if (p.x.multiply(q.z).subtract(q.x.multiply(p.z)).mod(P).signum() != 0) return false
        if (p.y.multiply(q.z).subtract(q.y.multiply(p.z)).mod(P).signum() != 0) return false
        return true
    }
}
