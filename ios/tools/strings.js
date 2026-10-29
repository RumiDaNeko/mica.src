// Android's strings (English, and Vietnamese as the Android build generates it from src/Strings.vi.tsv) as iOS
// Localizable.strings, keyed by the Android names: node ios/tools/strings.js
// Run an Android build first (it writes the generated Vietnamese file).
const fs = require('fs')
const path = require('path')

const repo = path.resolve(__dirname, '..', '..')
const res = path.join(repo, 'android', 'app', 'src', 'main', 'res')
const generatedVi = path.join(repo, 'android', 'app', 'build', 'generated', 'micaRes', 'values-vi', 'strings.xml')

function read(file) {
  const out = {}
  if (!fs.existsSync(file)) return out
  const xml = fs.readFileSync(file, 'utf8')
  for (const m of xml.matchAll(/<string name="([^"]+)"[^>]*>([\s\S]*?)<\/string>/g)) out[m[1]] = m[2]
  return out
}

// Android resource text → the plain string, then iOS format specifiers (%1$s → %1$@, %s → %@).
function convert(s) {
  s = s.replace(/^"(.*)"$/s, '$1')
    .replace(/\\'/g, "'").replace(/\\"/g, '"').replace(/\\n/g, '\n').replace(/\\t/g, '\t').replace(/\\@/g, '@').replace(/\\\?/g, '?')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&')
  return s.replace(/%(\d+\$)?s/g, (_, n) => '%' + (n || '') + '@')
}

const quote = s => '"' + s.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\t/g, '\\t') + '"'

function write(lang, strings) {
  const dir = path.join(repo, 'ios', 'Mica', 'Resources', lang + '.lproj')
  fs.mkdirSync(dir, { recursive: true })
  const keys = Object.keys(strings).sort()
  const body = '/* Generated from the Android strings by ios/tools/strings.js. Do not edit. */\n' +
    keys.map(k => quote(k) + ' = ' + quote(convert(strings[k])) + ';').join('\n') + '\n'
  fs.writeFileSync(path.join(dir, 'Localizable.strings'), body)
  return keys.length
}

const en = Object.assign(read(path.join(res, 'values', 'strings.xml')), read(path.join(res, 'values', 'strings_ios.xml')))
const vi = Object.assign(read(generatedVi), read(path.join(res, 'values-vi', 'strings_android.xml')))
// Strings only the iOS app uses live in ios/Mica/Resources/strings-ios.json: { "key": ["English", "Tiếng Việt"] }.
const own = path.join(repo, 'ios', 'Mica', 'Resources', 'strings-ios.json')
if (fs.existsSync(own)) for (const [k, [e, v]] of Object.entries(JSON.parse(fs.readFileSync(own, 'utf8')))) {
  en[k] = e.replace(/%@/g, '%s').replace(/%(\d+)\$@/g, '%$1$s'); if (v) vi[k] = v.replace(/%@/g, '%s').replace(/%(\d+)\$@/g, '%$1$s')
}
if (!fs.existsSync(generatedVi)) console.warn('No generated Vietnamese strings; run an Android build first.')
const missing = Object.keys(en).filter(k => !(k in vi))
console.log(`en: ${write('en', en)} strings, vi: ${write('vi', vi)} strings` + (missing.length ? ` (${missing.length} English only: ${missing.slice(0, 8).join(', ')}…)` : ''))
