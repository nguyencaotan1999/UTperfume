// Copies the built SPA (web/dist) into the API's wwwroot so the API serves it (ADR-0012).
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const dist = resolve(here, '..', 'dist')
const target = resolve(here, '..', '..', 'src', 'UTperfume.Api', 'wwwroot')

if (!existsSync(dist)) throw new Error('web/dist not found. Run "npm run build" first.')
mkdirSync(target, { recursive: true })
for (const entry of readdirSync(target)) {
  if (entry !== '.gitkeep') rmSync(join(target, entry), { recursive: true, force: true })
}
cpSync(dist, target, { recursive: true })
console.log(`Copied ${dist} -> ${target}`)
