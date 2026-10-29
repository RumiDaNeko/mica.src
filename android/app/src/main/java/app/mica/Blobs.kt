package app.mica

import android.content.ContentValues
import android.content.Context
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.util.Base64
import android.webkit.URLUtil
import android.webkit.WebView
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.security.SecureRandom

/**
 * Downloads a page made itself (`blob:` and `data:` addresses: exported PDFs, canvas pictures, generated
 * CSV), which Android's DownloadManager can't fetch. A blob only exists inside its page, so the page reads it
 * and hands it over in pieces through `micaBlob`, each piece carrying a one-time token Mica gave it.
 */
class Blobs(private val context: Context, private val done: (String?, String) -> Unit) {
    private class Job(var name: String, val file: File, val out: FileOutputStream, var mime: String, var size: Long = 0)
    private val jobs = HashMap<String, Job>()
    private val random = SecureRandom()

    fun start(view: WebView, url: String, contentDisposition: String?, mime: String?) {
        val token = ByteArray(12).also { random.nextBytes(it) }.joinToString("") { "%02x".format(it) }
        val name = URLUtil.guessFileName(url, contentDisposition, mime).let { if (it.endsWith(".bin") && mime.isNullOrBlank()) "download" else it }
        val file = File(context.cacheDir, "blob-$token")
        jobs[token] = Job(name, file, FileOutputStream(file), mime ?: "application/octet-stream")
        view.evaluateJavascript(SCRIPT.replace("URL", JSONObject.quote(url)).replace("TOKEN", JSONObject.quote(token)), null)
    }

    /** A message from `micaBlob`: "token\tstart\tmime\tname", "token\tdata\tbase64", "token\tend", "token\tfail". */
    fun onMessage(data: String) {
        val parts = data.split('\t', limit = 3)
        val job = jobs[parts[0]] ?: return
        try {
            when (parts.getOrNull(1)) {
                "start" -> {
                    val rest = (parts.getOrNull(2) ?: "").split('\t', limit = 2)
                    rest[0].takeIf { it.isNotBlank() }?.let { job.mime = it }
                    // The name the page gave the link (download="…"), without any folders in it.
                    rest.getOrNull(1)?.substringAfterLast('/')?.substringAfterLast('\\')?.trim()?.take(120)
                        ?.takeIf { it.isNotEmpty() && it != "." && it != ".." }?.let { job.name = it }
                }
                "data" -> {
                    val bytes = Base64.decode(parts.getOrNull(2) ?: "", Base64.DEFAULT)
                    job.size += bytes.size
                    if (job.size > MAX) throw IllegalStateException("too big")
                    job.out.write(bytes)
                }
                "end" -> { job.out.close(); jobs.remove(parts[0]); finish(job.name, job.mime, job.file) }
                else -> throw IllegalStateException(parts.getOrNull(2) ?: "failed")
            }
        } catch (e: Exception) {
            jobs.remove(parts[0])
            runCatching { job.out.close() }
            job.file.delete()
            done(null, job.name)
        }
    }

    /** Into Downloads, where the system's Files app shows it. */
    private fun finish(name: String, mime: String, file: File) {
        try {
            if (Build.VERSION.SDK_INT >= 29) {
                val resolver = context.contentResolver
                val values = ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, name)
                    put(MediaStore.Downloads.MIME_TYPE, mime)
                    put(MediaStore.Downloads.IS_PENDING, 1)
                }
                val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: throw IllegalStateException()
                resolver.openOutputStream(uri)!!.use { out -> file.inputStream().use { it.copyTo(out) } }
                values.clear(); values.put(MediaStore.Downloads.IS_PENDING, 0)
                resolver.update(uri, values, null, null)
            } else {
                // Before Android 10 public Downloads needs a storage permission; Mica's own folder there doesn't.
                val dir = context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS) ?: throw IllegalStateException()
                file.copyTo(File(dir, name), overwrite = true)
            }
            done(name, name)
        } catch (e: Exception) {
            done(null, name)
        } finally { file.delete() }
    }

    companion object {
        /** At document start: remembers the name a download link asks for, which Android's download listener never sees. */
        const val NAME_SCRIPT = """(()=>{if(window.__micaDlHook)return;window.__micaDlHook=1;const keep=a=>{if(a&&a.hasAttribute&&a.hasAttribute('download'))window.__micaDl={h:a.href,n:a.getAttribute('download')}};
const c=HTMLAnchorElement.prototype.click;HTMLAnchorElement.prototype.click=function(){keep(this);return c.apply(this,arguments)};
addEventListener('click',e=>{keep(e.target&&e.target.closest&&e.target.closest('a[download]'))},true)})()"""
        const val MAX = 200L * 1024 * 1024
        private const val SCRIPT = """(async()=>{const T=TOKEN;if(typeof micaBlob==='undefined')return;try{const b=await (await fetch(URL)).blob();const d=window.__micaDl;micaBlob.postMessage(T+'\tstart\t'+(b.type||'')+'\t'+(d&&d.h===URL?d.n:''));
const a=new Uint8Array(await b.arrayBuffer());for(let i=0;i<a.length;i+=393216){const c=a.subarray(i,i+393216);let s='';for(let j=0;j<c.length;j+=32768)s+=String.fromCharCode.apply(null,c.subarray(j,j+32768));micaBlob.postMessage(T+'\tdata\t'+btoa(s))}
micaBlob.postMessage(T+'\tend')}catch(e){micaBlob.postMessage(T+'\tfail\t'+e)}})()"""
    }
}
