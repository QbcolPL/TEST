package com.fieldtakhub.hub.core

/**
 * Callsign rules shared by ATAK and Meshtastic.
 *
 * ATAK gets the full callsign. Meshtastic gets it as the long name (max 39 bytes) and a
 * short name of at most 4 characters. The short-name generator keeps the shape of the
 * full name: the number stays at the end, a name that fits is kept whole, multi-word
 * names start with their initials, and Polish letters are folded to ASCII.
 */
object Callsign {
    const val SHORT_MAX = 4
    const val LONG_MAX_BYTES = 39
    private const val VOWELS = "AEIOUY"

    data class Parts(val words: List<String>, val number: String)

    fun split(full: String): Parts {
        val folded = TextFold.ascii(full).uppercase()
        val tokens = Regex("[A-Z]+|[0-9]+").findAll(folded).map { it.value }.toList()
        val words = tokens.filter { it[0].isLetter() }
        var digits = tokens.filter { it[0].isDigit() }.joinToString("")
        if (digits.length > 3) digits = digits.takeLast(3)
        return Parts(words, digits)
    }

    /** Best suggestion first. Empty when the callsign has no letters or digits. */
    fun suggestions(full: String): List<String> {
        val parts = split(full)
        val out = LinkedHashSet<String>()
        if (parts.words.isEmpty()) {
            if (parts.number.isNotEmpty()) out += parts.number.takeLast(SHORT_MAX)
            return out.toList()
        }
        val number = parts.number
        val budget = (SHORT_MAX - number.length).coerceAtLeast(1)
        val num = number.take(SHORT_MAX - 1)
        val joined = parts.words.joinToString("")

        if (parts.words.size == 1) {
            val w = parts.words[0]
            if (w.length <= budget) out += w + num
            out += skeleton(w, budget) + num
            out += w.take(budget) + num
            if (budget >= 2 && w.length > budget + 1) out += (w.take(1) + w.takeLast(1)).take(budget) + num
        } else {
            val initials = parts.words.joinToString("") { it.first().toString() }
            if (joined.length <= budget) out += joined + num
            if (initials.length >= budget) {
                out += initials.take(budget) + num
            } else {
                val need = budget - initials.length
                val last = parts.words.last()
                out += initials + skeletonTail(last, need) + num
                val first = parts.words.first()
                val rest = parts.words.drop(1).joinToString("") { it.first().toString() }
                out += (first.take(1) + skeletonTail(first, need) + rest).take(budget) + num
                out += (first.take(1 + need) + rest).take(budget) + num
            }
        }
        if (num.isNotEmpty() && budget > 1) out += skeleton(joined, budget - 1) + number.take(SHORT_MAX - (budget - 1))
        return out.filter { it.isNotEmpty() && it.length <= SHORT_MAX }.distinct()
    }

    fun suggest(full: String): String = suggestions(full).firstOrNull().orEmpty()

    /** First letter, then consonants (no immediate repeats), then vowels if still short; original order kept. */
    fun skeleton(word: String, length: Int): String {
        if (word.length <= length) return word
        val picked = sortedSetOf(0)
        var prev = word[0]
        for (i in 1 until word.length) {
            if (picked.size >= length) break
            val c = word[i]
            if (c !in VOWELS && c != prev) picked += i
            if (c !in VOWELS) prev = c
        }
        var i = 1
        while (picked.size < length && i < word.length) {
            if (i !in picked && word[i] in VOWELS) picked += i
            i++
        }
        i = 1
        while (picked.size < length && i < word.length) {
            if (i !in picked) picked += i
            i++
        }
        return picked.map { word[it] }.joinToString("")
    }

    /** Letters after the first one, consonants first, used to extend initials. */
    private fun skeletonTail(word: String, count: Int): String {
        if (count <= 0 || word.length <= 1) return ""
        return skeleton(word, count + 1).drop(1).take(count)
    }

    /** Cleans a short name typed by the user: ASCII letters and digits only, max 4. */
    fun cleanShort(input: String): String =
        TextFold.ascii(input).filter { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' }.take(SHORT_MAX)

    fun cleanFull(input: String): String = input.replace(Regex("\\s+"), " ").trim().take(40)

    /** Meshtastic long name: the full callsign, cut on a character boundary to 39 UTF-8 bytes. */
    fun longName(full: String): String {
        val clean = cleanFull(full)
        val sb = StringBuilder()
        var bytes = 0
        var i = 0
        while (i < clean.length) {
            val cp = clean.codePointAt(i)
            val s = String(Character.toChars(cp))
            val n = s.toByteArray(Charsets.UTF_8).size
            if (bytes + n > LONG_MAX_BYTES) break
            sb.append(s)
            bytes += n
            i += Character.charCount(cp)
        }
        return sb.toString()
    }
}
