package app.mica

import android.content.Context
import android.util.AtomicFile
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

/**
 * Highlights, from desktop's BrowserHighlights.cs: select text, choose Highlight, and it's kept per page on
 * this device (files/highlights.json) and put back each time the page loads. The reader keys them by the
 * article's address, so a highlight made in reading mode shows on the page too, and the other way round.
 */
class Highlights(context: Context) {
    class Mark(val id: String, val text: String, val color: String, val at: Long, val title: String)
    class Item(val url: String, val mark: Mark)

    private val file = AtomicFile(File(context.filesDir, "highlights.json"))
    private val pages: MutableMap<String, MutableList<Mark>> = load()

    private fun load(): MutableMap<String, MutableList<Mark>> {
        val out = LinkedHashMap<String, MutableList<Mark>>()
        try {
            val o = JSONObject(file.readFully().toString(Charsets.UTF_8))
            for (url in o.keys()) {
                val a = o.getJSONArray(url)
                out[url] = (0 until a.length()).map { a.getJSONObject(it).let { m -> Mark(m.getString("id"), m.getString("text"), m.optString("color", COLOR), m.optLong("at"), m.optString("title")) } }.toMutableList()
            }
        } catch (e: Exception) {}
        return out
    }

    private fun save() {
        val o = JSONObject()
        for ((url, list) in pages) if (list.isNotEmpty()) o.put(url, JSONArray().apply { list.forEach { put(JSONObject().put("id", it.id).put("text", it.text).put("color", it.color).put("at", it.at).put("title", it.title)) } })
        val out = file.startWrite()
        try { out.write(o.toString().toByteArray()); file.finishWrite(out) } catch (e: Exception) { file.failWrite(out) }
    }

    fun on(url: String): List<Mark> = pages[key(url)].orEmpty()

    /** Every highlight, newest first, for the library. */
    fun all(): List<Item> = pages.flatMap { (url, list) -> list.map { Item(url, it) } }.sortedByDescending { it.mark.at }

    fun add(url: String, text: String, id: String, title: String) {
        pages.getOrPut(key(url)) { mutableListOf() }.add(Mark(id, text, COLOR, System.currentTimeMillis(), title))
        save()
    }

    fun remove(item: Item) {
        pages[item.url]?.let { list -> list.removeAll { it.id == item.mark.id }; if (list.isEmpty()) pages.remove(item.url) }
        save()
    }

    companion object {
        const val COLOR = "#FFE26A"

        /** The page a highlight belongs to: its address without the #fragment. */
        fun key(url: String) = url.substringBefore('#')

        fun newId() = UUID.randomUUID().toString().replace("-", "").take(12)

        private const val LIB = """
const micaWrap=(range,id,color)=>{let root=range.commonAncestorContainer;if(root.nodeType===3)root=root.parentNode;const nodes=[];
 const w=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);let n;while(n=w.nextNode()){if(range.intersectsNode(n)&&n.data.trim())nodes.push(n);}
 const start=range.startContainer,so=range.startOffset,end=range.endContainer,eo=range.endOffset;let count=0;
 for(const t of nodes){const s=t===start?so:0,e=t===end?eo:t.data.length;if(s>=e)continue;let node=t;if(e<node.data.length)node.splitText(e);if(s>0)node=node.splitText(s);
  const m=document.createElement('mark');m.dataset.micaHl=id;m.style.cssText='background:'+color+';color:inherit;border-radius:2px;padding:0;box-shadow:0 0 0 1px '+color;node.parentNode.insertBefore(m,node);m.appendChild(node);count++;}
 return count>0;};
const micaFind=(q)=>{const walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT,{acceptNode:n=>/^(SCRIPT|STYLE|NOSCRIPT|TEXTAREA)$/.test(n.parentNode.nodeName)?2:1});
 let text='',map=[],n,space=true;while(n=walker.nextNode()){const d=n.data;for(let i=0;i<d.length;i++){const c=d[i];if(/\s/.test(c)){if(space)continue;space=true;text+=' ';map.push([n,i]);}else{space=false;text+=c;map.push([n,i]);}}}
 const at=text.indexOf(q);if(at<0)return null;const a=map[at],b=map[at+q.length-1];const r=document.createRange();r.setStart(a[0],a[1]);r.setEnd(b[0],b[1]+1);return r;};"""

        /** Marks the selection; the script's value is the text marked, or null. */
        fun markScript(id: String): String =
            "(()=>{$LIB const s=getSelection();if(!s||s.isCollapsed||!s.rangeCount)return null;const r=s.getRangeAt(0);const text=s.toString().replace(/\\s+/g,' ').trim();" +
                "if(!text||text.length>5000)return null;if(!micaWrap(r,${JSONObject.quote(id)},${JSONObject.quote(COLOR + "99")}))return null;s.removeAllRanges();return text;})()"

        /** After each load: puts back what was marked on the page. */
        fun restoreScript(marks: List<Mark>): String {
            val items = JSONArray().apply { marks.forEach { put(JSONObject().put("id", it.id).put("text", it.text).put("color", it.color)) } }
            return "(()=>{$LIB let done=0;for(const h of $items){if(document.querySelector('[data-mica-hl=\"'+h.id+'\"]')){done++;continue;}" +
                "const q=h.text.replace(/\\s+/g,' ').trim();const r=q&&micaFind(q);if(r&&micaWrap(r,h.id,h.color+'99'))done++;}return done;})()"
        }

        /** Takes a highlight off the page it's on, leaving the text. */
        fun unmarkScript(id: String) =
            "(()=>{for(const m of document.querySelectorAll('[data-mica-hl='+${JSONObject.quote(JSONObject.quote(id))}+']')){const p=m.parentNode;while(m.firstChild)p.insertBefore(m.firstChild,m);p.removeChild(m);p.normalize();}})()"
    }
}
