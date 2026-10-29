package app.mica

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import android.webkit.GeolocationPermissions
import android.webkit.PermissionRequest
import androidx.core.content.ContextCompat

/**
 * A site asking for the camera, microphone, location, protected media or MIDI. The island opens into the
 * question (desktop's site bubble); the answer is kept per site and kind, and can be taken back in Settings.
 * Android's own permission is asked only after you allow the site, and only for what it needs.
 */
class SitePermissions(private val activity: MainActivity) {
    enum class Kind(val key: String, val label: Int, val android: Array<String>) {
        CAMERA("camera", R.string.perm_camera, arrayOf(Manifest.permission.CAMERA)),
        MICROPHONE("microphone", R.string.perm_microphone, arrayOf(Manifest.permission.RECORD_AUDIO)),
        LOCATION("location", R.string.perm_location, arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)),
        PROTECTED_MEDIA("protected", R.string.perm_protected, emptyArray()),
        MIDI("midi", R.string.perm_midi, emptyArray());

        companion object { fun of(key: String) = values().firstOrNull { it.key == key } }
    }

    class Decision(val origin: String, val kind: Kind, val allow: Boolean)

    private val prefs get() = (activity.application as MicaApp).prefs
    private var asking: PermissionRequest? = null

    // ---- What's remembered ("origin\tkind\t1|0") ----

    fun decisions() = decisions(prefs)

    private fun decided(origin: String, kind: Kind): Boolean? = decisions().firstOrNull { it.origin == origin && it.kind == kind }?.allow

    private fun remember(origin: String, kinds: Collection<Kind>, allow: Boolean) {
        val keep = prefs.sitePermissions.filterNot { line -> kinds.any { line.startsWith(origin + "\t" + it.key + "\t") } }
        prefs.sitePermissions = (keep + kinds.map { origin + "\t" + it.key + "\t" + if (allow) "1" else "0" }).toSet()
    }


    // ---- Requests from pages ----

    fun request(tab: Tab, request: PermissionRequest) {
        val origin = originOf(request.origin)
        val wanted = request.resources.mapNotNull { r ->
            when (r) {
                PermissionRequest.RESOURCE_VIDEO_CAPTURE -> Kind.CAMERA
                PermissionRequest.RESOURCE_AUDIO_CAPTURE -> Kind.MICROPHONE
                PermissionRequest.RESOURCE_PROTECTED_MEDIA_ID -> Kind.PROTECTED_MEDIA
                PermissionRequest.RESOURCE_MIDI_SYSEX -> Kind.MIDI
                else -> null
            }?.let { it to r }
        }
        // Only the tab in front may ask, and only a secure page (a plain-http copy can't reuse a choice).
        if (origin == null || wanted.isEmpty() || tab !== activity.browser.active) return request.deny()
        fun answer(kinds: List<Kind>) {
            val resources = wanted.filter { it.first in kinds }.map { it.second }.toTypedArray()
            if (resources.isEmpty()) request.deny() else request.grant(resources)
        }
        val kinds = wanted.map { it.first }.distinct()
        val known = kinds.associateWith { decided(origin, it) }
        if (known.values.none { it == null }) return withAndroid(kinds.filter { known[it] == true }) { answer(it) }
        asking = request
        ask(origin, kinds) { allow ->
            asking = null
            remember(origin, kinds, allow)
            if (!allow) request.deny() else withAndroid(kinds) { answer(it) }
        }
    }

    fun canceled(request: PermissionRequest) { if (asking === request) { asking = null; activity.bubble.hide() } }

    fun geolocation(tab: Tab, rawOrigin: String, callback: GeolocationPermissions.Callback) {
        val origin = originOf(Uri.parse(rawOrigin))
        if (origin == null || tab !== activity.browser.active) return callback.invoke(rawOrigin, false, false)
        when (decided(origin, Kind.LOCATION)) {
            true -> withAndroid(listOf(Kind.LOCATION)) { callback.invoke(rawOrigin, it.isNotEmpty(), false) }
            false -> callback.invoke(rawOrigin, false, false)
            null -> ask(origin, listOf(Kind.LOCATION)) { allow ->
                remember(origin, listOf(Kind.LOCATION), allow)
                if (!allow) callback.invoke(rawOrigin, false, false) else withAndroid(listOf(Kind.LOCATION)) { callback.invoke(rawOrigin, it.isNotEmpty(), false) }
            }
        }
    }

    private fun ask(origin: String, kinds: List<Kind>, done: (Boolean) -> Unit) {
        var answered = false
        val what = kinds.joinToString(", ") { activity.getString(it.label) }
        activity.bubble.show(activity.getString(R.string.site_wants, Uri.parse(origin).host ?: origin), what, listOf(
            CardAction(activity.getString(R.string.block)) { if (!answered) { answered = true; done(false) } },
            CardAction(activity.getString(R.string.allow), true) { if (!answered) { answered = true; done(true) } }),
            seconds = 0, stays = true)
    }

    /** Asks Android for what the allowed kinds need; calls back with the kinds Mica may actually use. */
    private fun withAndroid(kinds: List<Kind>, done: (List<Kind>) -> Unit) {
        val missing = kinds.flatMap { it.android.toList() }.filter { ContextCompat.checkSelfPermission(activity, it) != PackageManager.PERMISSION_GRANTED }
        if (missing.isEmpty()) return done(kinds)
        activity.requestAndroidPermissions(missing.toTypedArray()) { granted ->
            val usable = kinds.filter { k -> k.android.isEmpty() || k.android.any { granted[it] == true || ContextCompat.checkSelfPermission(activity, it) == PackageManager.PERMISSION_GRANTED } }
            if (usable.size < kinds.size) activity.island.announce(activity.getString(R.string.android_blocked, kinds.filterNot { it in usable }.joinToString(", ") { activity.getString(it.label) }))
            done(usable)
        }
    }

    companion object {
        fun decisions(prefs: Prefs): List<Decision> = prefs.sitePermissions.mapNotNull { line ->
            val p = line.split('\t')
            if (p.size != 3) null else Kind.of(p[1])?.let { Decision(p[0], it, p[2] == "1") }
        }.sortedWith(compareBy({ it.origin }, { it.kind.ordinal }))

        fun forget(prefs: Prefs, d: Decision) {
            prefs.sitePermissions = prefs.sitePermissions.filterNot { it.startsWith(d.origin + "\t" + d.kind.key + "\t") }.toSet()
        }

        /** scheme://host[:port] for an https page (or localhost while developing); null for anything else. */
        fun originOf(u: Uri?): String? {
            val host = u?.host?.lowercase() ?: return null
            val secure = u.scheme == "https" || (u.scheme == "http" && (host == "localhost" || host == "127.0.0.1"))
            if (!secure) return null
            return u.scheme + "://" + host + (if (u.port > 0) ":" + u.port else "")
        }
    }
}
