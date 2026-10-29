package app.mica.account

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.AtomicFile
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Small secrets kept on this device only (desktop uses Windows DPAPI for the same things): the sign-in
 * token, this device's vault key for autofill, logins saved while the vault is locked. AES-GCM with a key
 * that never leaves the Android Keystore; the file is iv(12) + ciphertext.
 */
class Sealed(private val file: File, private val alias: String) {
    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(alias, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build())
        return gen.generateKey()
    }

    val exists get() = file.exists()

    fun read(): String? {
        if (!file.exists()) return null
        return try {
            val data = file.readBytes()
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, data, 0, 12))
            val plain = c.doFinal(data, 12, data.size - 12)
            plain.toString(Charsets.UTF_8).also { plain.fill(0) }
        } catch (e: Exception) { null }
    }

    fun write(text: String) {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key())
        val plain = text.toByteArray(Charsets.UTF_8)
        val sealed = try { c.iv + c.doFinal(plain) } finally { plain.fill(0) }
        val af = AtomicFile(file)
        val out = af.startWrite()
        try { out.write(sealed); af.finishWrite(out) } catch (e: Exception) { af.failWrite(out); throw e }
    }

    fun delete() { if (file.exists()) { try { file.writeBytes(ByteArray(file.length().toInt())) } catch (e: Exception) {}; file.delete() } }
}
