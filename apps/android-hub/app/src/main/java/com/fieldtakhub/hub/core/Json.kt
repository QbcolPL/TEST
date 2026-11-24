package com.fieldtakhub.hub.core

/**
 * Minimal, dependency-free JSON reader/writer.
 *
 * Builder writes JSON in several styles (PascalCase and camelCase keys, UTF-8 BOM,
 * \u escapes for Polish letters), so lookups through [JsonObj] are case-insensitive
 * and accept several alternative key names.
 */
class JsonException(message: String) : Exception(message)

object Json {
    fun parse(text: String): Any? {
        val p = Parser(text)
        p.skipWs()
        val value = p.readValue()
        p.skipWs()
        if (!p.atEnd()) throw JsonException("Unexpected data after JSON value at ${p.pos}")
        return value
    }

    fun parseObject(text: String): JsonObj {
        val v = parse(text)
        if (v !is Map<*, *>) throw JsonException("JSON root is not an object")
        @Suppress("UNCHECKED_CAST")
        return JsonObj(v as Map<String, Any?>)
    }

    fun parseObjectOrNull(text: String?): JsonObj? =
        if (text.isNullOrBlank()) null else try { parseObject(text) } catch (e: JsonException) { null }

    fun write(value: Any?, indent: Boolean = false): String {
        val sb = StringBuilder()
        writeValue(sb, value, if (indent) 0 else -1)
        return sb.toString()
    }

    private fun writeValue(sb: StringBuilder, value: Any?, level: Int) {
        when (value) {
            null -> sb.append("null")
            is JsonObj -> writeValue(sb, value.map, level)
            is String -> writeString(sb, value)
            is Boolean -> sb.append(if (value) "true" else "false")
            is Int, is Long, is Short, is Byte -> sb.append(value.toString())
            is Double -> sb.append(if (value.isFinite()) formatDouble(value) else "null")
            is Float -> sb.append(if (value.isFinite()) formatDouble(value.toDouble()) else "null")
            is Number -> sb.append(value.toString())
            is Map<*, *> -> {
                sb.append('{')
                var first = true
                for ((k, v) in value) {
                    if (!first) sb.append(',')
                    first = false
                    newline(sb, level + 1)
                    writeString(sb, k.toString())
                    sb.append(if (level >= 0) ": " else ":")
                    writeValue(sb, v, if (level >= 0) level + 1 else -1)
                }
                if (!first) newline(sb, level)
                sb.append('}')
            }
            is Iterable<*> -> {
                sb.append('[')
                var first = true
                for (v in value) {
                    if (!first) sb.append(',')
                    first = false
                    newline(sb, level + 1)
                    writeValue(sb, v, if (level >= 0) level + 1 else -1)
                }
                if (!first) newline(sb, level)
                sb.append(']')
            }
            is Array<*> -> writeValue(sb, value.asList(), level)
            else -> writeString(sb, value.toString())
        }
    }

    private fun formatDouble(d: Double): String =
        if (d == Math.floor(d) && Math.abs(d) < 1e15) d.toLong().toString() else d.toString()

    private fun newline(sb: StringBuilder, level: Int) {
        if (level < 0) return
        sb.append('\n')
        repeat(level) { sb.append("  ") }
    }

    private fun writeString(sb: StringBuilder, s: String) {
        sb.append('"')
        for (c in s) {
            when (c) {
                '"' -> sb.append("\\\"")
                '\\' -> sb.append("\\\\")
                '\n' -> sb.append("\\n")
                '\r' -> sb.append("\\r")
                '\t' -> sb.append("\\t")
                '\b' -> sb.append("\\b")
                '\u000C' -> sb.append("\\f")
                else -> if (c < ' ') sb.append(String.format("\\u%04x", c.code)) else sb.append(c)
            }
        }
        sb.append('"')
    }

    private class Parser(private val s: String) {
        var pos = 0

        init {
            if (s.isNotEmpty() && s[0] == '﻿') pos = 1
        }

        fun atEnd() = pos >= s.length

        fun skipWs() {
            while (pos < s.length) {
                val c = s[pos]
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == '﻿') pos++ else break
            }
        }

        fun readValue(): Any? {
            if (pos >= s.length) throw JsonException("Unexpected end of JSON")
            return when (val c = s[pos]) {
                '{' -> readObject()
                '[' -> readArray()
                '"' -> readString()
                't' -> literal("true", true)
                'f' -> literal("false", false)
                'n' -> literal("null", null)
                else -> if (c == '-' || c in '0'..'9') readNumber() else throw JsonException("Unexpected character '$c' at $pos")
            }
        }

        private fun literal(word: String, value: Any?): Any? {
            if (!s.startsWith(word, pos)) throw JsonException("Invalid literal at $pos")
            pos += word.length
            return value
        }

        private fun readObject(): Map<String, Any?> {
            val map = LinkedHashMap<String, Any?>()
            pos++ // {
            skipWs()
            if (pos < s.length && s[pos] == '}') { pos++; return map }
            while (true) {
                skipWs()
                if (pos >= s.length || s[pos] != '"') throw JsonException("Expected key at $pos")
                val key = readString()
                skipWs()
                if (pos >= s.length || s[pos] != ':') throw JsonException("Expected ':' at $pos")
                pos++
                skipWs()
                map[key] = readValue()
                skipWs()
                if (pos >= s.length) throw JsonException("Unterminated object")
                when (s[pos]) {
                    ',' -> pos++
                    '}' -> { pos++; return map }
                    else -> throw JsonException("Expected ',' or '}' at $pos")
                }
            }
        }

        private fun readArray(): List<Any?> {
            val list = ArrayList<Any?>()
            pos++ // [
            skipWs()
            if (pos < s.length && s[pos] == ']') { pos++; return list }
            while (true) {
                skipWs()
                list.add(readValue())
                skipWs()
                if (pos >= s.length) throw JsonException("Unterminated array")
                when (s[pos]) {
                    ',' -> pos++
                    ']' -> { pos++; return list }
                    else -> throw JsonException("Expected ',' or ']' at $pos")
                }
            }
        }

        private fun readString(): String {
            pos++ // opening quote
            val sb = StringBuilder()
            while (true) {
                if (pos >= s.length) throw JsonException("Unterminated string")
                val c = s[pos++]
                when {
                    c == '"' -> return sb.toString()
                    c == '\\' -> {
                        if (pos >= s.length) throw JsonException("Bad escape")
                        when (val e = s[pos++]) {
                            '"' -> sb.append('"')
                            '\\' -> sb.append('\\')
                            '/' -> sb.append('/')
                            'b' -> sb.append('\b')
                            'f' -> sb.append('\u000C')
                            'n' -> sb.append('\n')
                            'r' -> sb.append('\r')
                            't' -> sb.append('\t')
                            'u' -> {
                                if (pos + 4 > s.length) throw JsonException("Bad unicode escape")
                                val code = s.substring(pos, pos + 4).toIntOrNull(16) ?: throw JsonException("Bad unicode escape")
                                sb.append(code.toChar())
                                pos += 4
                            }
                            else -> throw JsonException("Bad escape '\\$e'")
                        }
                    }
                    else -> sb.append(c)
                }
            }
        }

        private fun readNumber(): Number {
            val start = pos
            if (s[pos] == '-') pos++
            while (pos < s.length && s[pos] in '0'..'9') pos++
            var isDouble = false
            if (pos < s.length && s[pos] == '.') {
                isDouble = true
                pos++
                while (pos < s.length && s[pos] in '0'..'9') pos++
            }
            if (pos < s.length && (s[pos] == 'e' || s[pos] == 'E')) {
                isDouble = true
                pos++
                if (pos < s.length && (s[pos] == '+' || s[pos] == '-')) pos++
                while (pos < s.length && s[pos] in '0'..'9') pos++
            }
            val text = s.substring(start, pos)
            if (text == "-" || text.isEmpty()) throw JsonException("Bad number at $start")
            return if (isDouble) text.toDouble() else text.toLongOrNull() ?: text.toDouble()
        }
    }
}

/** Read-only view over a parsed JSON object with tolerant, case-insensitive access. */
class JsonObj(val map: Map<String, Any?>) {
    private val lower: Map<String, String> by lazy {
        val m = HashMap<String, String>()
        for (k in map.keys) m.putIfAbsent(k.lowercase(), k)
        m
    }

    fun has(vararg keys: String): Boolean = keys.any { raw(it) != null }

    fun raw(key: String): Any? {
        if (map.containsKey(key)) return map[key]
        val real = lower[key.lowercase()] ?: return null
        return map[real]
    }

    private fun first(keys: Array<out String>): Any? {
        for (k in keys) {
            val v = raw(k)
            if (v != null) return v
        }
        return null
    }

    fun str(vararg keys: String): String? {
        return when (val v = first(keys)) {
            null -> null
            is String -> v
            is Number, is Boolean -> v.toString()
            else -> null
        }
    }

    fun text(vararg keys: String): String = str(*keys)?.trim().orEmpty()

    fun long(vararg keys: String): Long? = when (val v = first(keys)) {
        is Number -> v.toLong()
        is String -> v.trim().toLongOrNull() ?: v.trim().toDoubleOrNull()?.toLong()
        else -> null
    }

    fun int(vararg keys: String): Int? = long(*keys)?.toInt()

    fun double(vararg keys: String): Double? = when (val v = first(keys)) {
        is Number -> v.toDouble()
        is String -> v.trim().replace(',', '.').toDoubleOrNull()
        else -> null
    }

    fun bool(vararg keys: String): Boolean? = when (val v = first(keys)) {
        is Boolean -> v
        is String -> when (v.trim().lowercase()) {
            "true", "1", "yes", "tak" -> true
            "false", "0", "no", "nie" -> false
            else -> null
        }
        is Number -> v.toInt() != 0
        else -> null
    }

    fun obj(vararg keys: String): JsonObj? {
        val v = first(keys)
        @Suppress("UNCHECKED_CAST")
        return if (v is Map<*, *>) JsonObj(v as Map<String, Any?>) else null
    }

    fun objects(vararg keys: String): List<JsonObj> {
        val v = first(keys)
        if (v !is List<*>) return emptyList()
        @Suppress("UNCHECKED_CAST")
        return v.filterIsInstance<Map<*, *>>().map { JsonObj(it as Map<String, Any?>) }
    }

    fun strings(vararg keys: String): List<String> {
        val v = first(keys)
        if (v !is List<*>) return emptyList()
        return v.mapNotNull { if (it is String) it else it?.toString() }
    }
}
