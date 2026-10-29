// Everything the iOS app shares with desktop and Android, copied into ios/Mica/Resources/Shared, plus the
// WebKit blocker list and the strings: node ios/tools/prepare.js (before building, and before pushing ios/).
const fs = require('fs')
const path = require('path')
const { execFileSync } = require('child_process')

const repo = path.resolve(__dirname, '..', '..')
const shared = path.join(repo, 'ios', 'Mica', 'Resources', 'Shared')
fs.rmSync(shared, { recursive: true, force: true })
fs.mkdirSync(path.join(shared, 'games'), { recursive: true })
fs.mkdirSync(path.join(shared, 'scripts'), { recursive: true })

// Same list as gamePages in android/app/build.gradle.kts.
const games = ['GameHub', 'Tetris', 'Snake', 'Game2048', 'Minesweeper', 'Breakout', 'Sudoku', 'Memory', 'Flappy', 'Pong', 'Invaders', 'ConnectFour']
for (const g of games) fs.copyFileSync(path.join(repo, 'src', g + '.html'), path.join(shared, 'games', g + '.html'))
fs.copyFileSync(path.join(repo, 'src', 'Reader.js'), path.join(shared, 'scripts', 'Reader.js'))
const androidAssets = path.join(repo, 'android', 'app', 'src', 'main', 'assets')
for (const s of ['login', 'totp', 'passkey']) fs.copyFileSync(path.join(androidAssets, 'scripts', s + '.js'), path.join(shared, 'scripts', s + '.js'))
fs.copyFileSync(path.join(androidAssets, 'psl.dat'), path.join(shared, 'psl.dat'))
console.log(`shared: ${games.length} games, 4 scripts, psl.dat`)

for (const tool of ['blocker-rules.js', 'strings.js']) execFileSync(process.execPath, [path.join(__dirname, tool)], { stdio: 'inherit' })
