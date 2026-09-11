/// <reference types="vite/client" />

/**
 * The app's first read of build-time configuration, which is why this file exists now and not at M0.
 *
 * `vite/client` is where `import.meta.env` gets its type, and until M3 nothing in `src/` read an environment
 * variable, so nothing needed it. The consequence is worth knowing rather than ignoring: without this reference,
 * `import.meta.env` is a type error (TS2339) while every Vitest run stays green — vitest does not typecheck, so the
 * only thing that catches it is `npm run typecheck`, which is the first step of `npm run verify` for exactly this
 * reason.
 *
 * The interface below is declared rather than inferred from the index signature in `vite/client` so that a typo in a
 * variable name is a type error instead of `undefined`: `VITE_API_BASE_URL` is the only build-time input the app
 * reads, and AC-2's claim ("flag off changes nothing") depends on there being exactly one.
 */
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
