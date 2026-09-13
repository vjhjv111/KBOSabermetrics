import * as THREE from "three";

export type PlayerSurfaces = {
  cloth: THREE.Texture | null;
  skin: THREE.Texture | null;
  leather: THREE.Texture | null;
  wood: THREE.Texture | null;
};
export type PlayerSurfaceKind = keyof PlayerSurfaces;

const SIZE = 256;
const TAU = Math.PI * 2;
type TemplateKey = `${"height" | "roughness"}:${PlayerSurfaceKind}`;
// Only these eight deterministic images can be retained (2 MiB at 256² RGBA).
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
  // These are height data, never diffuse images: applying sRGB changes their slope.
  texture.colorSpace = THREE.NoColorSpace;
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
        const noise = periodicNoise(0x7bcde812, 64);
        return (u, v) => {
        const warp = Math.cos(u * TAU * 64);
        const weft = Math.cos(v * TAU * 64);
        const crossover = Math.sin(u * TAU * 32) * Math.sin(v * TAU * 32);
        return 128 + warp * 9 + weft * 9 + crossover * 5 + noise(u, v) * 5;
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
        const fine = periodicNoise(0x38b769aa, 64);
        const soft = periodicNoise(0x901edd57, 16);
        return (u, v) => {
        const crease = Math.pow(Math.abs(fine(u, v)), 0.6);
        return 143 - crease * 28 + soft(u, v) * 6;
        };
      }, 2);
    }
    case "wood": {
      return heightTexture("height:wood", () => {
        const noise = periodicNoise(0xa91ae481, 32);
        return (u, v) => {
        const bend = Math.sin(v * TAU) * 0.8 + Math.sin(v * TAU * 3) * 0.2;
        const longGrain = Math.sin(u * TAU * 28 + bend);
        const fineGrain = Math.sin(u * TAU * 61 + bend * 1.4);
        return 128 + longGrain * 12 + fineGrain * 5 + noise(u, v) * 2;
        };
      }, 1);
    }
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
  return 237+n*10+wide*5;
  };
 },kind==="cloth"?3:kind==="wood"?1:2);
}
