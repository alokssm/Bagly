import sharp from 'sharp'
import { mkdir } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const source = join(root, 'public/icons/icon.svg')
const outDir = join(root, 'public/icons')

await mkdir(outDir, { recursive: true })

for (const size of [192, 512]) {
  await sharp(source)
    .resize(size, size)
    .png()
    .toFile(join(outDir, `icon-${size}.png`))
}

console.log('Generated PWA icons in public/icons/')
