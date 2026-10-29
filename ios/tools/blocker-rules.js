// Converts the blocker's declarativeNetRequest lists (extensions/mica-blocker/rules, the ones default.json turns on)
// into one WebKit content-blocker list for iOS: node ios/tools/blocker-rules.js
// WebKit has no request-time hook, so the rules have to be in its own format. What it can't express is left out
// (counted at the end): regex filters, request methods, redirects, header changes, excluded request domains, and
// rules that need both included and excluded sites.
const fs = require('fs')
const path = require('path')

const repo = path.resolve(__dirname, '..', '..')
const rulesDir = path.join(repo, 'extensions', 'mica-blocker', 'rules')
const out = path.join(repo, 'ios', 'Mica', 'Resources', 'Blocker', 'rules.json')

const lists = JSON.parse(fs.readFileSync(path.join(rulesDir, 'default.json'), 'utf8')).lists
const skipped = {}
const skip = why => { skipped[why] = (skipped[why] || 0) + 1; return null }

// declarativeNetRequest resource types → WebKit's. Documents are handled apart (sub-frames only).
const TYPES = {
  stylesheet: 'style-sheet', script: 'script', image: 'image', font: 'font', media: 'media',
  xmlhttprequest: 'raw', websocket: 'raw', other: 'raw', ping: 'ping', object: 'raw', csp_report: 'raw',
}
const ALL_TYPES = Object.keys(TYPES).concat('sub_frame')

// "||ads.example.com^*/banner" → a regular expression in the subset WebKit accepts.
function urlFilterRegex(filter) {
  if (/[^\x20-\x7e]/.test(filter)) return null
  let f = filter
  let prefix = '', suffix = ''
  // WebKit's subset has no alternation or counted repeats, so a literal { } or | can't be trusted to compile.
  if (/[{}|]/.test(f.replace(/^\|\|?/, '').replace(/\|$/, ''))) return null
  if (f.startsWith('||')) { prefix = '^https?://([^/]*\\.)?'; f = f.slice(2) }
  else if (f.startsWith('|')) { prefix = '^'; f = f.slice(1) }
  if (f.endsWith('|')) { suffix = '$'; f = f.slice(0, -1) }
  // A trailing separator also matches the end of the address, which WebKit can't say, so it's dropped there.
  while (f.endsWith('^')) f = f.slice(0, -1)
  let re = ''
  for (const c of f) {
    if (c === '*') re += '.*'
    else if (c === '^') re += '[^a-zA-Z0-9_.%-]'
    else if ('.+?()[]{}$\\/|'.includes(c)) re += '\\' + c
    else re += c
  }
  re = prefix + re + suffix
  if (re === '' || re === '.*') re = '.*'
  return re
}

function domainRegex(d) {
  return '^https?://([^/]*\\.)?' + d.replace(/[.+?()[\]{}$\\/|*^]/g, m => '\\' + m) + '[:/?#]'
}

function convert(rule) {
  const a = rule.action.type, c = rule.condition
  if (!['block', 'allow', 'allowAllRequests'].includes(a)) return skip('action ' + a)
  if (c.regexFilter) return skip('regexFilter')
  if (c.requestMethods || c.excludedRequestMethods) return skip('requestMethods')
  if (c.excludedRequestDomains) return skip('excludedRequestDomains')
  if (c.initiatorDomains && c.excludedInitiatorDomains) return skip('included and excluded sites')
  if (c.requestDomains && c.urlFilter) return skip('requestDomains with urlFilter')
  if (c.tabIds || c.excludedTabIds || c.responseHeaders) return skip('tab or header condition')

  let filters
  if (c.urlFilter) { const r = urlFilterRegex(c.urlFilter); if (!r) return skip('urlFilter WebKit cannot express'); filters = [r] }
  else if (c.requestDomains) filters = c.requestDomains.filter(d => !/[^\x20-\x7e]/.test(d)).map(domainRegex)
  else filters = ['.*']
  if (!filters.length) return skip('non-ASCII domains')

  let types = c.resourceTypes || ALL_TYPES.filter(t => !(c.excludedResourceTypes || []).includes(t))
  if (a === 'allowAllRequests') types = ALL_TYPES
  const frames = types.includes('sub_frame')
  const plain = [...new Set(types.map(t => TYPES[t]).filter(Boolean))]

  const base = {}
  if (c.isUrlFilterCaseSensitive) base['url-filter-is-case-sensitive'] = true
  if (c.initiatorDomains) base['if-domain'] = c.initiatorDomains.filter(d => !/[^\x20-\x7e]/.test(d)).map(d => '*' + d)
  if (c.excludedInitiatorDomains) base['unless-domain'] = c.excludedInitiatorDomains.filter(d => !/[^\x20-\x7e]/.test(d)).map(d => '*' + d)
  if (base['if-domain'] && !base['if-domain'].length) return skip('non-ASCII sites')
  if (c.domainType === 'thirdParty') base['load-type'] = ['third-party']
  if (c.domainType === 'firstParty') base['load-type'] = ['first-party']

  const action = { type: a === 'block' ? 'block' : 'ignore-previous-rules' }
  const result = []
  // A whole-domain rule also blocks pages on that domain: one rule per domain keeps the list under WebKit's
  // limit. Address patterns only ever match frames inside a page, never the page you opened.
  const domainOnly = !c.urlFilter && !!c.requestDomains
  for (const filter of filters) {
    if (domainOnly && frames) { result.push({ trigger: { 'url-filter': filter, ...base, 'resource-type': plain.concat('document') }, action }); continue }
    if (plain.length) result.push({ trigger: { 'url-filter': filter, ...base, 'resource-type': plain }, action })
    if (frames) result.push({ trigger: { 'url-filter': filter, ...base, 'resource-type': ['document'], 'load-context': ['child-frame'] }, action })
  }
  return result.length ? { rule, result } : skip('no resource types WebKit knows')
}

const block = [], allow = []
let total = 0
for (const name of lists) {
  for (const rule of JSON.parse(fs.readFileSync(path.join(rulesDir, name + '.json'), 'utf8'))) {
    total++
    const r = convert(rule)
    if (r) (rule.action.type === 'block' ? block : allow).push(r)
  }
}
// WebKit applies rules in order and "ignore-previous-rules" undoes earlier matches, so blocks go first by priority
// and every allow comes after them.
const byPriority = (x, y) => (x.rule.priority || 1) - (y.rule.priority || 1)
block.sort(byPriority); allow.sort(byPriority)
const rules = block.concat(allow).flatMap(r => r.result)
fs.mkdirSync(path.dirname(out), { recursive: true })
fs.writeFileSync(out, JSON.stringify(rules))
// For Settings ("17913 rules from 6 lists"), without reading the whole list.
fs.writeFileSync(path.join(path.dirname(out), 'meta.json'), JSON.stringify({ rules: total, lists: lists.length, webkit: rules.length }))
console.log(`${total} rules in ${lists.length} lists → ${rules.length} WebKit rules (${(fs.statSync(out).size / 1024 / 1024).toFixed(1)} MB)`)
console.log('left out:', skipped)
