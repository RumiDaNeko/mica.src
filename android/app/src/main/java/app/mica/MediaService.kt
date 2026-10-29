package app.mica

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.MediaMetadata
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.os.Build
import android.os.IBinder
import android.util.Log

/**
 * Keeps a page's audio or video going with the screen off or the app in the background: a media
 * foreground service with a MediaSession, so the lock screen, notification and headset buttons can play,
 * pause and stop it. Runs only while a tab has media (playing, or paused from here); stops as soon as
 * there's none, so it costs nothing otherwise.
 */
class MediaService : Service() {
    private lateinit var session: MediaSession
    private val browser get() = (application as MicaApp).browser

    override fun onCreate() {
        super.onCreate()
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CHANNEL, getString(R.string.media_channel), NotificationManager.IMPORTANCE_LOW).apply { setShowBadge(false) })
        session = MediaSession(this, "Mica").apply {
            setCallback(object : MediaSession.Callback() {
                override fun onPlay() { browser.mediaPlay(); refresh() }
                override fun onPause() { browser.mediaPause(); refresh() }
                override fun onStop() { browser.mediaStop(); refresh() }
            })
            isActive = true
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_PLAY -> browser.mediaPlay()
            ACTION_PAUSE -> browser.mediaPause()
            ACTION_STOP -> browser.mediaStop()
        }
        refresh()
        return START_NOT_STICKY
    }

    private fun refresh() {
        val tab = browser.mediaTab
        if (tab == null) {
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return
        }
        val playing = tab.playing && !browser.mediaHeld
        val title = tab.title.ifBlank { Navigation.label(tab.url) }
        session.setMetadata(MediaMetadata.Builder()
            .putString(MediaMetadata.METADATA_KEY_TITLE, title)
            .putString(MediaMetadata.METADATA_KEY_ARTIST, Navigation.label(tab.url))
            .apply { tab.icon?.let { putBitmap(MediaMetadata.METADATA_KEY_ART, it) } }
            .build())
        session.setPlaybackState(PlaybackState.Builder()
            .setActions(PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE or PlaybackState.ACTION_PLAY_PAUSE or PlaybackState.ACTION_STOP)
            .setState(if (playing) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED, PlaybackState.PLAYBACK_POSITION_UNKNOWN, 1f)
            .build())

        fun action(icon: Int, label: Int, what: String) = Notification.Action.Builder(
            android.graphics.drawable.Icon.createWithResource(this, icon), getString(label),
            PendingIntent.getService(this, what.hashCode(), Intent(this, MediaService::class.java).setAction(what), PendingIntent.FLAG_IMMUTABLE)).build()
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), PendingIntent.FLAG_IMMUTABLE)
        val n = Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_stat_mica)
            .setContentTitle(title)
            .setContentText(Navigation.label(tab.url))
            .setLargeIcon(tab.icon)
            .setContentIntent(open)
            .setOngoing(playing)
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .addAction(if (playing) action(R.drawable.ic_pause, R.string.pause, ACTION_PAUSE) else action(R.drawable.ic_play, R.string.play, ACTION_PLAY))
            .addAction(action(R.drawable.ic_stop, R.string.stop, ACTION_STOP))
            .setStyle(Notification.MediaStyle().setMediaSession(session.sessionToken).setShowActionsInCompactView(0, 1))
            .build()
        try {
            if (Build.VERSION.SDK_INT >= 29) startForeground(ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK) else startForeground(ID, n)
            // Paused: let the notification be swiped away and the system reclaim the service.
            if (!playing) stopForeground(STOP_FOREGROUND_DETACH)
        } catch (e: Exception) {
            Log.w("Mica", "media notification: $e")
        }
    }

    override fun onDestroy() {
        session.release()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        private const val CHANNEL = "media"
        private const val ID = 7
        const val ACTION_PLAY = "app.mica.PLAY"
        const val ACTION_PAUSE = "app.mica.PAUSE"
        const val ACTION_STOP = "app.mica.STOP"

        /** Starts, refreshes or stops the service to match the browser's media tab. */
        fun update(context: Context) {
            val browser = (context.applicationContext as MicaApp).browser
            val intent = Intent(context, MediaService::class.java)
            try {
                if (browser.mediaTab != null) {
                    if (browser.mediaTab?.playing == true) context.startForegroundService(intent) else context.startService(intent)
                } else context.startService(intent)
            } catch (e: Exception) {
                // Android 12+ won't start a foreground service from the background; the next play from the
                // page while Mica is in front starts it.
                Log.w("Mica", "media service: $e")
            }
        }
    }
}
