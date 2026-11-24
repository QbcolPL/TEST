package com.fieldtakhub.hub.core

/** Dotted numeric version, e.g. "5.6.0.12 (bebdc3f4)" -> [5, 6, 0, 12]. */
class Version(val parts: List<Int>) : Comparable<Version> {
    override fun compareTo(other: Version): Int = compareParts(other, maxOf(parts.size, other.parts.size))

    /** Compares only the first [precision] parts; missing parts count as 0. */
    fun compareParts(other: Version, precision: Int): Int {
        for (i in 0 until precision) {
            val a = parts.getOrElse(i) { 0 }
            val b = other.parts.getOrElse(i) { 0 }
            if (a != b) return a.compareTo(b)
        }
        return 0
    }

    override fun equals(other: Any?): Boolean = other is Version && compareTo(other) == 0
    override fun hashCode(): Int = parts.dropLastWhile { it == 0 }.hashCode()
    override fun toString(): String = parts.joinToString(".")
}

/**
 * Version requirement as written by Builder:
 *  "5.0+" / ">=5.1" -> minimum, "5.6-5.8" -> range, "5.6.0" -> minimum,
 *  "Zgodna z paczką .FTAK" -> no version information.
 */
data class VersionRequirement(val min: Version?, val max: Version?, val raw: String) {
    fun isSatisfiedBy(installed: Version): Boolean {
        if (min != null && installed < min) return false
        if (max != null && installed.compareParts(max, max.parts.size) > 0) return false
        return true
    }

    fun describe(): String = when {
        min != null && max != null -> "$min – $max"
        min != null -> "≥ $min"
        max != null -> "≤ $max"
        else -> raw
    }
}

object Versions {
    private val number = Regex("(\\d+(?:\\.\\d+){0,4})")

    fun parse(text: String?): Version? {
        if (text.isNullOrBlank()) return null
        val m = number.find(text) ?: return null
        return Version(m.groupValues[1].split('.').mapNotNull { it.toIntOrNull() })
    }

    fun requirement(raw: String?, maxRaw: String? = null): VersionRequirement? {
        val text = raw?.trim().orEmpty()
        val max = parse(maxRaw)
        val range = Regex("^\\s*v?(\\d+(?:\\.\\d+)*)\\s*[-–]\\s*v?(\\d+(?:\\.\\d+)*)\\s*$").find(text)
        if (range != null) {
            return VersionRequirement(parse(range.groupValues[1]), parse(range.groupValues[2]) ?: max, text)
        }
        val min = parse(text)
        if (min == null && max == null) return null
        return VersionRequirement(min, max, text)
    }
}
