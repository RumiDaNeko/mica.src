import Foundation
import CommonCrypto
import CryptoKit
import Security

/// A message for the user (android's IllegalStateException / IllegalArgumentException).
struct Problem: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

/// A failed call: the HTTP status and the service's {"error": code}, when it sent one.
struct ApiError: LocalizedError {
    let status: Int
    let code: String?
    var errorDescription: String? { "Sync service error \(status)" + (code.map { " (\($0))" } ?? "") }
}

struct VaultError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
    var wrongPassword: Bool { message.hasPrefix("Wrong") }
}

/// Small secrets kept on this device only (desktop uses DPAPI, Android the Keystore): the sign-in token, this
/// device's vault key for autofill, logins saved while the vault is locked. In the Keychain, this device only.
struct Sealed {
    let name: String
    private var query: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "online.lessnoise.mica", kSecAttrAccount as String: name]
    }

    var exists: Bool { SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess }

    func read() -> String? {
        var q = query
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    func write(_ text: String) throws {
        let data = Data(text.utf8)
        let update: [String: Any] = [kSecValueData as String: data, kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        var status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var add = query
            add.merge(update) { $1 }
            status = SecItemAdd(add as CFDictionary, nil)
        }
        if status != errSecSuccess { throw Problem("Keychain error \(status)") }
    }

    func delete() { SecItemDelete(query as CFDictionary) }
}

// MARK: - Envelope and crypto

/// Desktop's vault, byte for byte (src\VaultModel.cs), so a vault synced from Windows or Android opens here and back:
/// PBKDF2-HMAC-SHA256(UTF-8 password, 16-byte salt, 600,000 rounds, 64 bytes) → AES-256-CBC/PKCS7 key (first half)
/// and HMAC-SHA256 key (second half); the MAC covers "MicaVault\n1\nPBKDF2-SHA256\n600000\n" + salt + "\n" + iv + "\n"
/// + ciphertext (standard base64), checked before decrypting.
struct Envelope {
    var format: Int, kdf: String, iterations: Int, salt: String, iv: String, ciphertext: String, mac: String

    var map: [String: Any] { ["format": format, "kdf": kdf, "iterations": iterations, "salt": salt, "iv": iv, "ciphertext": ciphertext, "mac": mac] }
    var json: String { JsonOut.write(map) }

    /// Only the seven authenticated fields; a local inbox (desktop-only) is ignored.
    static func from(_ m: [String: Any]) -> Envelope {
        Envelope(format: (m["format"] as? NSNumber)?.intValue ?? 0, kdf: m["kdf"] as? String ?? "", iterations: (m["iterations"] as? NSNumber)?.intValue ?? 0,
                 salt: m["salt"] as? String ?? "", iv: m["iv"] as? String ?? "", ciphertext: m["ciphertext"] as? String ?? "", mac: m["mac"] as? String ?? "")
    }

    static func parse(_ data: Data) throws -> Envelope {
        guard let m = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw VaultError("Invalid vault format.") }
        return from(m)
    }
}

enum VaultCrypto {
    static let iterations = 600_000
    static let kdf = "PBKDF2-SHA256"
    private static let keyPrefix = "\u{1}MicaVaultKey1:"

    static func randomBytes(_ n: Int) -> Data {
        var d = Data(count: n)
        _ = d.withUnsafeMutableBytes { SecRandomCopyBytes(kSecRandomDefault, n, $0.baseAddress!) }
        return d
    }

    static func base64Url(_ d: Data) -> String {
        d.base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
    }

    /// Sign-in: a random verifier and its SHA-256 challenge (both unpadded base64url).
    static func randomVerifier() -> String { base64Url(randomBytes(32)) }
    static func challenge(_ verifier: String) -> String { base64Url(Data(SHA256.hash(data: Data(verifier.utf8)))) }

    /// The site part of an HTTPS address as the vault keeps it: lower-case https://host[:port].
    static func origin(_ value: String) throws -> String {
        let bad = Problem("Enter an HTTPS website address without user information.")
        guard let u = URLComponents(string: value.trimmingCharacters(in: .whitespacesAndNewlines)), u.scheme?.lowercased() == "https",
              let host = u.host, !host.isEmpty, u.user == nil, u.password == nil else { throw bad }
        let port = (u.port == nil || u.port == 443) ? "" : ":\(u.port!)"
        return "https://\(host)\(port)".lowercased()
    }

    static func originOrNil(_ value: String) -> String? { try? origin(value) }

    static func pbkdf2(_ password: Data, salt: Data, rounds: Int, length: Int) -> Data {
        var out = Data(count: length)
        let status = out.withUnsafeMutableBytes { o in
            password.withUnsafeBytes { p in
                salt.withUnsafeBytes { s in
                    CCKeyDerivationPBKDF(CCPBKDFAlgorithm(kCCPBKDF2), p.baseAddress?.assumingMemoryBound(to: Int8.self), password.count,
                                         s.baseAddress?.assumingMemoryBound(to: UInt8.self), salt.count, CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256),
                                         UInt32(rounds), o.baseAddress?.assumingMemoryBound(to: UInt8.self), length)
                }
            }
        }
        precondition(status == kCCSuccess)
        return out
    }

    private static func derive(_ password: String, salt: Data) throws -> Data {
        if password.isEmpty { throw VaultError("Enter your vault master password.") }
        return pbkdf2(Data(password.utf8), salt: salt, rounds: iterations, length: 64)
    }

    static func isKey(_ secret: String?) -> Bool { secret?.hasPrefix(keyPrefix) == true }
    private static func keyText(_ salt: Data, _ key: Data) -> String { keyPrefix + salt.base64EncodedString() + ":" + key.base64EncodedString() }
    private static func keyPart(_ secret: String, _ index: Int, _ size: Int) throws -> Data {
        let parts = secret.dropFirst(keyPrefix.count).split(separator: ":", omittingEmptySubsequences: false)
        if parts.count != 2 { throw VaultError("Invalid vault key.") }
        return try decode(String(parts[index]), size)
    }

    /// A password or a vault key → the 64-byte key for this salt.
    private static func key(_ secret: String, salt: Data) throws -> Data {
        if !isKey(secret) { return try derive(secret, salt: salt) }
        if try keyPart(secret, 0, 16) != salt { throw VaultError("Wrong master password or damaged vault.") }
        return try keyPart(secret, 1, 64)
    }

    /// A vault key for a new master password (new salt).
    static func newKey(_ password: String) throws -> String { let salt = randomBytes(16); return keyText(salt, try derive(password, salt: salt)) }
    /// A random vault key, for a vault created before the user picks a master password.
    static func newDeviceKey() -> String { keyText(randomBytes(16), randomBytes(64)) }

    /// Checks a password (or key) against the vault and returns its vault key.
    static func keyFor(_ e: Envelope, _ secret: String) throws -> String {
        try validate(e)
        let salt = try decode(e.salt, 16)
        let k = try key(secret, salt: salt)
        try authenticate(e, k)
        return isKey(secret) ? secret : keyText(salt, k)
    }

    private static func canonical(_ e: Envelope) -> Data {
        Data(("MicaVault\n\(e.format)\n\(e.kdf)\n\(e.iterations)\n\(e.salt)\n\(e.iv)\n\(e.ciphertext)").utf8)
    }

    private static func hmac(_ key: Data, _ data: Data) -> Data { Data(HMAC<SHA256>.authenticationCode(for: data, using: SymmetricKey(data: key))) }

    private static func authenticate(_ e: Envelope, _ key: Data) throws {
        let expected = hmac(key.subdata(in: 32..<64), canonical(e))
        let given = try decode(e.mac, 32)
        // Constant time, as MessageDigest.isEqual.
        var diff: UInt8 = 0
        for (a, b) in zip(expected, given) { diff |= a ^ b }
        if diff != 0 || expected.count != given.count { throw VaultError("Wrong master password or damaged vault.") }
    }

    /// Standard padded base64 that re-encodes to itself, of an exact size when [size] > 0.
    private static func decode(_ s: String, _ size: Int) throws -> Data {
        guard let b = Data(base64Encoded: s), b.base64EncodedString() == s, size <= 0 || b.count == size else { throw VaultError("Invalid vault format.") }
        return b
    }

    static func validate(_ e: Envelope) throws {
        if e.format != 1 || e.kdf != kdf || e.iterations != iterations { throw VaultError("Unsupported vault format.") }
        _ = try decode(e.salt, 16); _ = try decode(e.iv, 16); _ = try decode(e.mac, 32)
        let c = try decode(e.ciphertext, 0)
        if c.isEmpty || c.count % 16 != 0 || c.count > 4 * 1024 * 1024 { throw VaultError("Invalid vault size.") }
    }

    private static func aes(_ op: Int, key: Data, iv: Data, _ input: Data) throws -> Data {
        var out = Data(count: input.count + kCCBlockSizeAES128)
        var moved = 0
        let cap = out.count
        let status = out.withUnsafeMutableBytes { o in
            input.withUnsafeBytes { i in
                key.withUnsafeBytes { k in
                    iv.withUnsafeBytes { v in
                        CCCrypt(CCOperation(op), CCAlgorithm(kCCAlgorithmAES), CCOptions(kCCOptionPKCS7Padding), k.baseAddress, key.count, v.baseAddress,
                                i.baseAddress, input.count, o.baseAddress, cap, &moved)
                    }
                }
            }
        }
        if status != kCCSuccess { throw VaultError("Wrong master password or damaged vault.") }
        return out.prefix(moved)
    }

    static func encrypt(_ payload: VaultPayload, _ secret: String) throws -> Envelope {
        try payload.validate()
        let salt = try isKey(secret) ? keyPart(secret, 0, 16) : randomBytes(16)
        let iv = randomBytes(16)
        let k = try key(secret, salt: salt)
        let cipher = try aes(kCCEncrypt, key: k.prefix(32), iv: iv, Data(payload.json.utf8))
        var e = Envelope(format: 1, kdf: kdf, iterations: iterations, salt: salt.base64EncodedString(), iv: iv.base64EncodedString(), ciphertext: cipher.base64EncodedString(), mac: "")
        e.mac = hmac(k.subdata(in: 32..<64), canonical(e)).base64EncodedString()
        return e
    }

    static func decrypt(_ e: Envelope, _ secret: String) throws -> VaultPayload {
        try validate(e)
        let k = try key(secret, salt: try decode(e.salt, 16))
        try authenticate(e, k)
        let plain = try aes(kCCDecrypt, key: k.prefix(32), iv: try decode(e.iv, 16), try decode(e.ciphertext, 0))
        let p = try VaultPayload.parse(plain)
        try p.validate()
        return p
    }
}

// MARK: - Payload

/// One login. [saved] is when it was last saved here or on another device (ms since 1970; 0 for logins saved
/// before sync merged), which decides which copy wins a merge. Fields Mica for iOS doesn't know ride along in [extra].
final class VaultEntry {
    var id: String, origin: String, username: String, password: String, totp: String?, saved: Int64
    private let extra: [String: Any]

    init(id: String, origin: String, username: String, password: String, totp: String? = nil, saved: Int64 = 0, extra: [String: Any] = [:]) {
        self.id = id; self.origin = origin; self.username = username; self.password = password; self.totp = totp; self.saved = saved; self.extra = extra
    }

    var map: [String: Any] {
        var m = extra
        m["Id"] = id; m["Origin"] = origin; m["Username"] = username; m["Password"] = password; m["Totp"] = totp ?? NSNull(); m["Saved"] = saved
        return m
    }

    /// Lowercase origin, newline, username: how two copies of the vault recognise the same login (as desktop's LoginKey).
    var key: String { origin.lowercased() + "\n" + username }

    func copy(id: String? = nil) -> VaultEntry { VaultEntry(id: id ?? self.id, origin: origin, username: username, password: password, totp: totp, saved: saved, extra: extra) }

    static func make(_ origin: String, _ username: String, _ password: String, totp: String? = nil) throws -> VaultEntry {
        VaultEntry(id: newId(), origin: try VaultCrypto.origin(origin), username: username, password: password, totp: totp)
    }

    static func newId() -> String { UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() }
}

/// The decrypted vault, in desktop's PascalCase JSON. What Mica here doesn't use (Snapshot, InboxPrivate, anything
/// newer) is kept as it came, so a round trip through this phone loses nothing desktop relies on.
final class VaultPayload {
    var raw: [String: Any]
    var entries: [VaultEntry] = []
    static let maxRemoved = 2000

    init(raw: [String: Any] = ["Format": 1]) { self.raw = raw }

    var accountId: String? {
        get { raw["AccountId"] as? String }
        set { raw["AccountId"] = newValue ?? NSNull() }
    }
    var revision: Int64 {
        get { (raw["Revision"] as? NSNumber)?.int64Value ?? 0 }
        set { raw["Revision"] = newValue }
    }
    /// Logins deleted on some device, as "origin\nusername\tms", so a merge doesn't bring them back.
    var removed: [String] {
        get { (raw["Removed"] as? [Any])?.compactMap { $0 as? String } ?? [] }
        set { raw["Removed"] = newValue }
    }

    static func now() -> Int64 { Int64(Date().timeIntervalSince1970 * 1000) }

    /// A login was saved (now): it no longer counts as deleted.
    func touch(_ e: VaultEntry, now: Int64 = VaultPayload.now()) {
        e.saved = now
        let k = e.key + "\t"
        if removed.contains(where: { $0.hasPrefix(k) }) { removed = removed.filter { !$0.hasPrefix(k) } }
    }

    /// A login was deleted (now): other copies drop it too when they merge with this one.
    func forget(_ e: VaultEntry, now: Int64 = VaultPayload.now()) {
        entries.removeAll { $0 === e }
        forgetKey(e.key, now: now)
    }

    /// As forget, for a login that is gone under its old origin or username (an edit renamed it).
    func forgetKey(_ key: String, now: Int64 = VaultPayload.now()) {
        let list = removed.filter { !$0.hasPrefix(key + "\t") } + [key + "\t\(now)"]
        removed = Array(list.suffix(VaultPayload.maxRemoved))
    }

    /// Desktop's MergePayload: another copy of this vault folds into this one. Every login either has, the newest
    /// save of a login both have, minus logins deleted at or after they were last saved. Returns how many changed here.
    @discardableResult
    func merge(_ from: VaultPayload) -> Int {
        let before = entries.count
        var replaced = 0
        var gone: [String: Int64] = [:]
        for r in removed + from.removed {
            guard let tab = r.lastIndex(of: "\t"), let at = Int64(r[r.index(after: tab)...]) else { continue }
            let k = String(r[..<tab])
            if (gone[k] ?? Int64.min) < at { gone[k] = at }
        }
        for e in from.entries {
            if e.password.isEmpty && (e.totp ?? "").isEmpty { continue }
            guard let same = entries.first(where: { $0.key == e.key }) else {
                entries.append(e.copy(id: e.id.isEmpty || entries.contains { $0.id == e.id } ? VaultEntry.newId() : e.id))
                continue
            }
            if e.saved > same.saved { same.password = e.password; same.totp = e.totp; same.saved = e.saved; replaced += 1 }
            else if (same.totp ?? "").isEmpty && !(e.totp ?? "").isEmpty { same.totp = e.totp }
        }
        entries.removeAll { x in gone[x.key].map { $0 >= x.saved } == true }
        removed = gone.sorted { $0.value < $1.value }.suffix(VaultPayload.maxRemoved).map { $0.key + "\t\($0.value)" }
        if raw["Snapshot"] == nil, let s = from.raw["Snapshot"] { raw["Snapshot"] = s }
        return max(0, entries.count - before) + replaced
    }

    /// The same logins and deletions as [other] (ids and order aside): nothing to upload or save.
    func sameLogins(_ other: VaultPayload) -> Bool {
        func sig(_ p: VaultPayload) -> ([String], [String]) {
            (p.entries.map { [$0.key, $0.password, $0.totp ?? "", String($0.saved)].joined(separator: "\u{0}") }.sorted(), p.removed.sorted())
        }
        return sig(self) == sig(other)
    }

    func validate() throws {
        if (raw["Format"] as? NSNumber)?.intValue != 1 || entries.count > 10000 { throw Problem("Invalid vault payload.") }
        for e in entries {
            if e.origin != (try? VaultCrypto.origin(e.origin)) || e.username.count > 4096 || e.password.count > 65536 || (e.totp?.count ?? 0) > 1024 {
                throw Problem("Invalid credential in the vault.")
            }
        }
    }

    var json: String {
        var out = raw
        out["Format"] = 1
        out["Entries"] = entries.map { $0.map }
        return JsonOut.write(out)
    }

    func clone() -> VaultPayload { (try? VaultPayload.parse(Data(json.utf8))) ?? VaultPayload() }

    static func parse(_ data: Data) throws -> VaultPayload {
        guard let map = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw Problem("Invalid vault payload.") }
        let p = VaultPayload(raw: map)
        for item in map["Entries"] as? [Any] ?? [] {
            guard let e = item as? [String: Any] else { continue }
            p.entries.append(VaultEntry(id: e["Id"] as? String ?? VaultEntry.newId(), origin: e["Origin"] as? String ?? "", username: e["Username"] as? String ?? "",
                                        password: e["Password"] as? String ?? "", totp: e["Totp"] as? String, saved: (e["Saved"] as? NSNumber)?.int64Value ?? 0, extra: e))
        }
        return p
    }
}

/// JSON the way desktop's JavaScriptSerializer reads it back: whole numbers without ".0", and its dates
/// ("/Date(ms)/" once parsed) written in the escaped "\/Date(ms)\/" form it requires.
enum JsonOut {
    static func write(_ value: Any?) -> String { var s = ""; write(&s, value); return s }

    private static func write(_ s: inout String, _ v: Any?) {
        switch v {
        case nil, is NSNull: s += "null"
        case let n as NSNumber:
            if CFGetTypeID(n) == CFBooleanGetTypeID() { s += n.boolValue ? "true" : "false"; return }
            let d = n.doubleValue
            if d == d.rounded() && abs(d) < 1e15 { s += String(n.int64Value) } else { s += String(d) }
        case let str as String:
            if str.hasPrefix("/Date("), str.hasSuffix(")/"), str.dropFirst(6).dropLast(2).allSatisfy({ $0.isNumber || $0 == "-" }) {
                s += "\"" + str.replacingOccurrences(of: "/", with: "\\/") + "\""
            } else { string(&s, str) }
        case let m as [String: Any]:
            s += "{"
            var first = true
            for (k, value) in m { if !first { s += "," }; first = false; string(&s, k); s += ":"; write(&s, value) }
            s += "}"
        case let a as [Any]:
            s += "["
            var first = true
            for x in a { if !first { s += "," }; first = false; write(&s, x) }
            s += "]"
        case let b as Bool: s += b ? "true" : "false"
        case let i as Int: s += String(i)
        case let i as Int64: s += String(i)
        default: string(&s, "\(v!)")
        }
    }

    private static func string(_ s: inout String, _ str: String) {
        s += "\""
        for u in str.unicodeScalars {
            switch u {
            case "\"": s += "\\\""
            case "\\": s += "\\\\"
            case "\n": s += "\\n"
            case "\r": s += "\\r"
            case "\t": s += "\\t"
            case "<", ">", "&", "'": s += String(format: "\\u%04x", u.value)
            default:
                if u.value < 0x20 { s += String(format: "\\u%04x", u.value) } else { s.unicodeScalars.append(u) }
            }
        }
        s += "\""
    }
}

// MARK: - Authenticator codes

/// Authenticator codes (RFC 6238), ported from desktop's src\Totp.cs: a site's 2FA setup key, kept with its login in
/// the vault as a normalized otpauth:// link, gives the code for the current time step.
enum Totp {
    struct Key { var secret: Data; var digits = 6; var period = 30; var algorithm = "SHA1"; var issuer: String? = nil; var account: String? = nil }

    private static let alphabet = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567")

    static func parse(_ text: String?) throws -> Key {
        guard let t = text?.trimmingCharacters(in: .whitespacesAndNewlines), !t.isEmpty else { throw Problem("Enter the site's 2FA setup key.") }
        if !t.lowercased().hasPrefix("otpauth://") { return try check(Key(secret: try base32(t))) }
        let unsupported = Problem("Only time-based (TOTP) 2FA keys are supported.")
        guard let u = URLComponents(string: t), u.host?.lowercased() == "totp" else { throw unsupported }
        let label = u.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        var issuer: String?, account: String?
        if let colon = label.firstIndex(of: ":") {
            issuer = label[..<colon].trimmingCharacters(in: .whitespaces); account = label[label.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        } else if !label.isEmpty { account = label.trimmingCharacters(in: .whitespaces) }
        var secret: String?
        var k = Key(secret: Data())
        for pair in (u.percentEncodedQuery ?? "").split(separator: "&") {
            guard let at = pair.firstIndex(of: "="), at != pair.startIndex else { continue }
            let name = pair[..<at].lowercased()
            let value = String(pair[pair.index(after: at)...]).replacingOccurrences(of: "+", with: " ").removingPercentEncoding ?? ""
            switch name {
            case "secret": secret = value
            case "issuer": if !value.isEmpty { issuer = value }
            case "digits": if let n = Int(value) { k.digits = n }
            case "period": if let n = Int(value) { k.period = n }
            case "algorithm": k.algorithm = value.uppercased()
            default: break
            }
        }
        k.secret = try base32(secret)
        k.issuer = issuer
        k.account = account
        return try check(k)
    }

    private static func check(_ k: Key) throws -> Key {
        if k.digits < 6 || k.digits > 8 { throw Problem("This 2FA key asks for \(k.digits)-digit codes, which Mica can't make.") }
        if k.period < 10 || k.period > 300 { throw Problem("This 2FA key has an unusual time step.") }
        if !["SHA1", "SHA256", "SHA512"].contains(k.algorithm) { throw Problem("This 2FA key uses an algorithm Mica doesn't know.") }
        return k
    }

    static func parseOrNil(_ text: String?) -> Key? { try? parse(text) }

    /// Spaces, dashes and padding are ignored; letters may be any case.
    static func base32(_ text: String?) throws -> Data {
        let clean = (text ?? "").filter { !$0.isWhitespace && $0 != "-" && $0 != "=" }.uppercased()
        let bad = Problem("That doesn't look like a 2FA setup key.")
        if clean.count < 16 || clean.count > 128 { throw bad }
        var bytes = Data()
        var buffer = 0, bits = 0
        for c in clean {
            guard let v = alphabet.firstIndex(of: c) else { throw bad }
            buffer = (buffer << 5) | v; bits += 5
            if bits >= 8 { bytes.append(UInt8((buffer >> (bits - 8)) & 0xFF)); bits -= 8; buffer &= (1 << bits) - 1 }
        }
        return bytes
    }

    private static func toBase32(_ bytes: Data) -> String {
        var s = ""
        var buffer = 0, bits = 0
        for b in bytes {
            buffer = (buffer << 8) | Int(b); bits += 8
            while bits >= 5 { s.append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5 }
            buffer &= (1 << bits) - 1
        }
        if bits > 0 { s.append(alphabet[(buffer << (5 - bits)) & 31]) }
        return s
    }

    /// .NET's Uri.EscapeDataString: everything but RFC 3986 unreserved characters is %-escaped.
    private static func escape(_ s: String) -> String {
        var out = ""
        for b in s.utf8 {
            let c = Character(UnicodeScalar(b))
            if (b >= 65 && b <= 90) || (b >= 97 && b <= 122) || (b >= 48 && b <= 57) || "-_.~".contains(c) { out.append(c) } else { out += String(format: "%%%02X", b) }
        }
        return out
    }

    /// How a key is kept in the vault: one form, whatever it was saved from.
    static func normalize(_ k: Key) -> String {
        "otpauth://totp/" + escape(k.account ?? "") + "?secret=" + toBase32(k.secret) + "&digits=\(k.digits)&period=\(k.period)&algorithm=" + k.algorithm +
            ((k.issuer ?? "").isEmpty ? "" : "&issuer=" + escape(k.issuer!))
    }

    static func code(_ k: Key, at unix: Int64) -> String {
        let step = UInt64(max(0, unix) / Int64(k.period))
        let counter = withUnsafeBytes(of: step.bigEndian) { Data($0) }
        let key = SymmetricKey(data: k.secret)
        let hash: Data
        switch k.algorithm {
        case "SHA256": hash = Data(HMAC<SHA256>.authenticationCode(for: counter, using: key))
        case "SHA512": hash = Data(HMAC<SHA512>.authenticationCode(for: counter, using: key))
        default: hash = Data(HMAC<Insecure.SHA1>.authenticationCode(for: counter, using: key))
        }
        let h = [UInt8](hash)
        let o = Int(h[h.count - 1] & 0x0F)
        let binary = (Int(h[o] & 0x7F) << 24) | (Int(h[o + 1]) << 16) | (Int(h[o + 2]) << 8) | Int(h[o + 3])
        var mod = 1
        for _ in 0..<k.digits { mod *= 10 }
        let s = String(binary % mod)
        return String(repeating: "0", count: max(0, k.digits - s.count)) + s
    }

    static func code(_ saved: String?, now: Int64 = Int64(Date().timeIntervalSince1970)) -> String? { parseOrNil(saved).map { code($0, at: now) } }
    static func secondsLeft(_ saved: String?, now: Int64 = Int64(Date().timeIntervalSince1970)) -> Int {
        parseOrNil(saved).map { $0.period - Int(now % Int64($0.period)) } ?? 0
    }
}

// MARK: - Import

/// Passwords exported from Chrome, Edge, Firefox, Bitwarden and the like, as desktop's BrowserImport reads them:
/// RFC 4180 CSV, header names matched loosely, HTTPS logins with a password only, one per site and user.
enum PasswordCsv {
    struct Login { let origin: String; let username: String; let password: String }

    static func parse(_ text: String) -> [Login] {
        let rows = self.rows(text.hasPrefix("\u{FEFF}") ? String(text.dropFirst()) : text)
        guard let head = rows.first?.map({ $0.trimmingCharacters(in: .whitespaces).lowercased() }) else { return [] }
        func col(_ names: String...) -> Int { head.firstIndex { names.contains($0) } ?? -1 }
        let url = col("url", "login_uri", "website", "origin", "web site")
        let user = col("username", "login_username", "user name", "login", "email")
        let pass = col("password", "login_password")
        if url < 0 || pass < 0 { return [] }
        var seen = Set<String>()
        var out: [Login] = []
        for r in rows.dropFirst() {
            func cell(_ i: Int) -> String { i >= 0 && i < r.count ? r[i] : "" }
            guard let origin = VaultCrypto.originOrNil(cell(url)) else { continue }
            let p = cell(pass)
            if p.isEmpty { continue }
            let u = cell(user)
            if seen.insert(origin + "\n" + u).inserted { out.append(Login(origin: origin, username: u, password: p)) }
        }
        return out
    }

    private static func rows(_ s: String) -> [[String]] {
        var rows: [[String]] = []
        var row: [String] = []
        var cell = ""
        var quoted = false
        var it = Array(s).makeIterator()
        var pending: Character? = nil
        while let c = pending ?? it.next() {
            pending = nil
            if quoted {
                if c == "\"" {
                    let n = it.next()
                    if n == "\"" { cell.append("\"") } else { quoted = false; pending = n }
                } else { cell.append(c) }
                continue
            }
            switch c {
            case "\"": quoted = true
            case ",": row.append(cell); cell = ""
            case "\r": break
            case "\n", "\r\n":
                row.append(cell); cell = ""
                if row.contains(where: { !$0.isEmpty }) { rows.append(row) }
                row = []
            default: cell.append(c)
            }
        }
        row.append(cell)
        if row.contains(where: { !$0.isEmpty }) { rows.append(row) }
        return rows
    }
}

// MARK: - The service

/// The Mica sync service (desktop's VaultApi): JSON in and out, no redirects, 15 s timeouts, 2 MiB at most either
/// way. Call off the main thread.
enum Api {
    static let base = "https://the-api.lessnoise.online"
    private static let limit = 2 * 1024 * 1024

    private final class NoRedirect: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                        completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    }

    private static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.requestCachePolicy = .reloadIgnoringLocalCacheData
        c.urlCache = nil
        c.httpCookieStorage = nil
        return URLSession(configuration: c, delegate: NoRedirect(), delegateQueue: nil)
    }()

    static func call(_ method: String, _ route: String, body: Any? = nil, token: String? = nil, timeout: TimeInterval = 15) throws -> [String: Any] {
        guard let url = URL(string: base + route) else { throw Problem("Bad address.") }
        var req = URLRequest(url: url, timeoutInterval: timeout)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let token { req.setValue("Bearer " + token, forHTTPHeaderField: "Authorization") }
        if let body {
            let bytes = Data(JsonOut.write(body).utf8)
            if bytes.count > limit { throw Problem("Too large to sync (2 MB limit).") }
            req.httpBody = bytes
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        var result: (Data?, URLResponse?, Error?) = (nil, nil, nil)
        let done = DispatchSemaphore(value: 0)
        session.dataTask(with: req) { d, r, e in result = (d, r, e); done.signal() }.resume()
        done.wait()
        if let e = result.2 { throw e }
        guard let http = result.1 as? HTTPURLResponse else { throw Problem("No response.") }
        let data = result.0 ?? Data()
        if data.count > limit { throw Problem("Sync response too large.") }
        let parsed = (try? JSONSerialization.jsonObject(with: data.isEmpty ? Data("{}".utf8) : data)) as? [String: Any]
        if !(200...299).contains(http.statusCode) { throw ApiError(status: http.statusCode, code: parsed?["error"] as? String) }
        return parsed ?? [:]
    }
}
