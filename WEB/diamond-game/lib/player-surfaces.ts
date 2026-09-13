import * as THREE from "three";

export type PlayerSurfaces = {
  cloth: THREE.Texture | null;
  skin: THREE.Texture | null;
  leather: THREE.Texture | null;
  wood: THREE.Texture | null;
};
export type PlayerSurfaceKind = keyof PlayerSurfaces;
export type PlayerAlbedoKind = Exclude<PlayerSurfaceKind, "skin">;

const SIZE = 256;
const TAU = Math.PI * 2;
type TemplateKey = `${"height" | "roughness"}:${PlayerSurfaceKind}` | `albedo:${PlayerAlbedoKind}`;
// Only these eleven deterministic images can be retained (2.75 MiB at 256² RGBA).
// Templates stay private: every canvas receives a copy, never this array.
const pixelTemplates: Partial<Record<TemplateKey, Uint8ClampedArray>> = Object.create(null);

function randomSequence(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let value = Math.imul(state ^ (state >>> 15), state | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
  };
}

// Periodic interpolation keeps the bump maps seamless under RepeatWrapping.
function periodicNoise(seed: number, cells: number): (u: number, v: number) => number {
  const random = randomSequence(seed);
  const values = Float32Array.from({ length: cells * cells }, () => random() * 2 - 1);
  const at = (x: number, y: number) => values[((y % cells + cells) % cells) * cells + (x % cells + cells) % cells];
  return (u, v) => {
    const x = u * cells;
    const y = v * cells;
    const left = Math.floor(x);
    const top = Math.floor(y);
    const fx = x - left;
    const fy = y - top;
    const sx = fx * fx * (3 - 2 * fx);
    const sy = fy * fy * (3 - 2 * fy);
    const a = at(left, top) * (1 - sx) + at(left + 1, top) * sx;
    const b = at(left, top + 1) * (1 - sx) + at(left + 1, top + 1) * sx;
    return a * (1 - sy) + b * sy;
  };
}

// A leather grain is made of irregular raised cells rather than round, evenly
// spaced dimples. Repeat the feature points themselves so UV seams stay closed.
function leatherGrain(seed: number, cells: number): (u: number, v: number) => number {
  const random = randomSequence(seed);
  const points = Float32Array.from({ length: cells * cells * 2 }, () => 0.18 + random() * 0.64);
  return (u, v) => {
    const x = u * cells, y = v * cells, left = Math.floor(x), top = Math.floor(y);
    let first = Infinity, second = Infinity;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const cx = left + dx, cy = top + dy;
      const at = (((cy % cells + cells) % cells) * cells + (cx % cells + cells) % cells) * 2;
      const px = cx + points[at] - x, py = cy + points[at + 1] - y;
      const distance = px * px + py * py;
      if (distance < first) { second = first; first = distance; }
      else if (distance < second) second = distance;
    }
    return 1 - Math.exp(-(second - first) * 12);
  };
}

function knitYarn(u: number, v: number): number {
  const course = v * TAU * 32;
  const wale = u * TAU * 48 + Math.sin(course) * 0.72;
  return Math.cos(wale) * (7 + Math.cos(course) * 2) + Math.cos(course * 2) * 3;
}

function woodGrowthLine(u: number, v: number): number {
  const bend = Math.sin(v * TAU) * 0.8 + Math.sin(v * TAU * 3) * 0.2;
  const growth = u * TAU * 22 + bend + Math.sin(u * TAU * 5) * 0.9 + Math.sin(u * TAU * 11) * 0.18;
  return Math.pow(Math.max(0, Math.sin(growth)), 4);
}

function heightTexture(key: TemplateKey, buildHeight: () => (u: number, v: number) => number, repeat: number): THREE.Texture | null {
  if (typeof document === "undefined") return null;
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = SIZE;
  const context = canvas.getContext("2d");
  if (!context) return null;
  const pixels = context.createImageData(SIZE, SIZE);
  let template = pixelTemplates[key];
  if (!template) {
    const height = buildHeight();
    template = new Uint8ClampedArray(SIZE * SIZE * 4);
    for (let y = 0; y < SIZE; y++) {
      for (let x = 0; x < SIZE; x++) {
        const value = Math.max(0, Math.min(255, Math.round(height((x + 0.5) / SIZE, (y + 0.5) / SIZE))));
        const offset = (y * SIZE + x) * 4;
        template[offset] = template[offset + 1] = template[offset + 2] = value;
        template[offset + 3] = 255;
      }
    }
    pixelTemplates[key] = template;
  }
  pixels.data.set(template);
  context.putImageData(pixels, 0, 0);
  const texture = new THREE.CanvasTexture(canvas);
  // Heights/roughness are linear data; neutral diffuse tints use the renderer's
  // normal sRGB decode. Sharing the sampler settings does not mix those spaces.
  texture.colorSpace = key.startsWith("albedo:") ? THREE.SRGBColorSpace : THREE.NoColorSpace;
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
  texture.repeat.set(repeat, repeat);
  texture.magFilter = THREE.LinearFilter;
  texture.minFilter = THREE.LinearMipmapLinearFilter;
  texture.anisotropy = 4;
  texture.generateMipmaps = true;
  return texture;
}

/**
 * Small, deterministic height maps for MeshStandardMaterial.bumpMap.
 * Construct once per player and share its maps across that player's materials.
 * The player/renderer owns these textures and must dispose each distinct map on
 * teardown. No texture changes or canvas work are needed during animation.
 */
export function createPlayerSurfaces(): PlayerSurfaces {
  return {
    cloth: createPlayerSurface("cloth"),
    skin: createPlayerSurface("skin"),
    leather: createPlayerSurface("leather"),
    wood: createPlayerSurface("wood"),
  };
}

/** Standalone equipment can own just the map it uses, with no unused textures. */
export function createPlayerSurface(kind: PlayerSurfaceKind): THREE.Texture | null {
  if (typeof document === "undefined") return null;
  switch (kind) {
    case "cloth": {
      return heightTexture("height:cloth", () => {
        const fibre = periodicNoise(0x7bcde812, 64);
        const tension = periodicNoise(0x32e81cd9, 8);
        return (u, v) => {
          // Stagger each knitted course: the old square grid read as embossed
          // plastic at close range. The broad term gives fabric slight variation
          // without increasing the tiny physical bump scale or adding geometry.
          return 128 + knitYarn(u, v) + tension(u, v) * 5 + fibre(u, v) * 3;
        };
      }, 3);
    }
    case "skin": {
      return heightTexture("height:skin", () => {
        const fine = periodicNoise(0x51cabc38, 96);
        const soft = periodicNoise(0xf829343b, 16);
        return (u, v) => {
        const pore = Math.max(0, fine(u, v) - 0.28);
        return 134 - pore * pore * 40 + soft(u, v) * 3;
        };
      }, 2);
    }
    case "leather": {
      return heightTexture("height:leather", () => {
        const grain = leatherGrain(0x417b892c, 32);
        const fine = periodicNoise(0x38b769aa, 64);
        const soft = periodicNoise(0x901edd57, 16);
        return (u, v) => {
          return 124 + grain(u, v) * 14 + soft(u, v) * 4 + fine(u, v) * 3;
        };
      }, 2);
    }
    case "wood": {
      return heightTexture("height:wood", () => {
        const noise = periodicNoise(0xa91ae481, 32);
        return (u, v) => {
          const bend = Math.sin(v * TAU) * 0.8 + Math.sin(v * TAU * 3) * 0.2;
          // Thin, uneven growth lines follow the bat's length. Wide regular
          // sinusoidal bands made the previous barrel look lathe-ridged.
          const longGrain = woodGrowthLine(u, v);
          const fineGrain = Math.sin(u * TAU * 73 + bend);
          return 132 - longGrain * 13 + fineGrain * 2 + noise(u, v) * 2;
        };
      }, 1);
    }
  }
}

/** Neutral diffuse tints retain the selected team/equipment color. The fabric
 * stays almost white; only wood and leather receive stronger pigment grain. */
export function createPlayerAlbedo(kind: PlayerAlbedoKind): THREE.Texture | null {
  switch (kind) {
    case "cloth":
      return heightTexture("albedo:cloth", () => {
        const fibre = periodicNoise(0x7bcde812, 64), tension = periodicNoise(0x32e81cd9, 8);
        return (u, v) => 250.5 + knitYarn(u, v) * 0.28 + tension(u, v) * 1.3 + fibre(u, v) * 0.7;
      }, 3);
    case "leather":
      return heightTexture("albedo:leather", () => {
        const grain = leatherGrain(0x417b892c, 32), soft = periodicNoise(0x901edd57, 16);
        return (u, v) => 237 + grain(u, v) * 11 + soft(u, v) * 4;
      }, 2);
    case "wood":
      return heightTexture("albedo:wood", () => {
        const noise = periodicNoise(0xa91ae481, 32);
        return (u, v) => 252 - woodGrowthLine(u, v) * 27 + noise(u, v) * 2;
      }, 1);
    default:
      return null;
  }
}

/** Roughness is independent of height, avoiding equally glossy cloth, skin and leather. */
export function createPlayerRoughness(kind:PlayerSurfaceKind):THREE.Texture|null{
 // Preserve the old runtime fallback for an unknown JS kind without letting
 // arbitrary caller strings grow the finite template cache.
 const pixelKind=kind==="skin"||kind==="leather"||kind==="wood"?kind:"cloth";
 return heightTexture(`roughness:${pixelKind}`,()=>{
  const fine=periodicNoise(0x729d4613,pixelKind==="skin"?48:32),broad=periodicNoise(0x384b7821,8);
  return (u,v)=>{
  const n=fine(u,v),wide=broad(u,v);
  if(pixelKind==="skin")return 207+n*14+wide*13;
  if(pixelKind==="leather")return 224+n*12-wide*10;
  if(pixelKind==="wood")return 216+n*8+Math.sin(u*TAU*28)*9;
  return 244+n*4+wide*4+Math.cos(v*TAU*64)*2;
  };
 },kind==="cloth"?3:kind==="wood"?1:2);
}
