package com.fieldtakhub.hub.core

import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.sin
import kotlin.math.sqrt
import kotlin.math.tan

/** WGS84 latitude/longitude to UTM and MGRS (1 m precision), valid for 80°S to 84°N. */
object Mgrs {
    data class Utm(val zone: Int, val band: Char, val easting: Double, val northing: Double)

    private const val A = 6378137.0
    private const val F = 1 / 298.257223563
    private const val K0 = 0.9996
    private const val E2 = F * (2 - F)
    private const val EP2 = E2 / (1 - E2)
    private const val BANDS = "CDEFGHJKLMNPQRSTUVWX"
    private const val ROWS = "ABCDEFGHJKLMNPQRSTUV"
    private val COLUMNS = arrayOf("ABCDEFGH", "JKLMNPQR", "STUVWXYZ")

    fun isSupported(lat: Double, lon: Double): Boolean = lat >= -80.0 && lat < 84.0 && lon >= -180.0 && lon <= 180.0

    fun toUtm(lat: Double, lonIn: Double): Utm {
        require(isSupported(lat, lonIn)) { "MGRS obsługuje szerokości od 80°S do 84°N" }
        val lon = if (lonIn >= 180.0) lonIn - 360.0 else lonIn
        var zone = floor((lon + 180.0) / 6.0).toInt() + 1
        if (lat >= 56.0 && lat < 64.0 && lon >= 3.0 && lon < 12.0) zone = 32
        if (lat >= 72.0 && lat < 84.0) {
            zone = when {
                lon >= 0.0 && lon < 9.0 -> 31
                lon >= 9.0 && lon < 21.0 -> 33
                lon >= 21.0 && lon < 33.0 -> 35
                lon >= 33.0 && lon < 42.0 -> 37
                else -> zone
            }
        }
        val lon0 = Math.toRadians((zone - 1) * 6.0 - 180.0 + 3.0)
        val phi = Math.toRadians(lat)
        val lam = Math.toRadians(lon)
        val sinPhi = sin(phi)
        val cosPhi = cos(phi)
        val n = A / sqrt(1 - E2 * sinPhi * sinPhi)
        val t = tan(phi) * tan(phi)
        val c = EP2 * cosPhi * cosPhi
        val a = cosPhi * (lam - lon0)
        val e4 = E2 * E2
        val e6 = e4 * E2
        val m = A * ((1 - E2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * phi -
            (3 * E2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * sin(2 * phi) +
            (15 * e4 / 256 + 45 * e6 / 1024) * sin(4 * phi) -
            (35 * e6 / 3072) * sin(6 * phi))
        val a2 = a * a
        val easting = K0 * n * (a + (1 - t + c) * a2 * a / 6 + (5 - 18 * t + t * t + 72 * c - 58 * EP2) * a2 * a2 * a / 120) + 500000.0
        var northing = K0 * (m + n * tan(phi) * (a2 / 2 + (5 - t + 9 * c + 4 * c * c) * a2 * a2 / 24 +
            (61 - 58 * t + t * t + 600 * c - 330 * EP2) * a2 * a2 * a2 / 720))
        if (lat < 0) northing += 10_000_000.0
        val bandIndex = floor((lat + 80.0) / 8.0).toInt().coerceIn(0, BANDS.length - 1)
        return Utm(zone, BANDS[bandIndex], easting, northing)
    }

    /** "34U CA 48120 21050" */
    fun format(lat: Double, lon: Double): String {
        val utm = toUtm(lat, lon)
        val set = ((utm.zone - 1) % 6) + 1
        val columns = COLUMNS[(set - 1) % 3]
        val colIndex = floor(utm.easting / 100_000.0).toInt().coerceIn(1, 8)
        val rowIndex = floor(utm.northing / 100_000.0).toInt() % 20
        val rowLetter = ROWS[(rowIndex + if (set % 2 == 0) 5 else 0) % 20]
        val e = floor(utm.easting).toLong() % 100_000
        val nn = floor(utm.northing).toLong() % 100_000
        return String.format(
            java.util.Locale.ROOT, "%d%c %c%c %05d %05d",
            utm.zone, utm.band, columns[colIndex - 1], rowLetter, e, nn
        )
    }

    fun formatLatLon(lat: Double, lon: Double): String =
        String.format(java.util.Locale.ROOT, "%.5f, %.5f", lat, lon)
}
