import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import testingLibrary from 'eslint-plugin-testing-library'
import tseslint from 'typescript-eslint'
import vitest from '@vitest/eslint-plugin'

/**
 * ESLint flat config — DEBT-03.
 *
 * The rule of this file: **lint for correctness, never for taste.** Formatting is deliberately absent
 * (no `prettier`, no stylistic rules) because a formatter would rewrite all 24 source files in a commit
 * that has nothing to do with M2b, which `AGENTS.md` forbids as unrelated churn. Add it as its own
 * commit, or not at all.
 *
 * Three rule groups were chosen because this repository has actually bled in those three classes — not
 * because a blog post said to enable them:
 *
 * 1. `typescript-eslint`'s **type-checked** rules. `tsc` proves a program compiles; it does not prove a
 *    `void promise` was awaited or that a branch is unreachable. That gap is why
 *    `@typescript-eslint/no-floating-promises` exists.
 * 2. `react-hooks`. `ApplicationsPage` holds a comment guarding a hook against sitting below an early
 *    `return`. A comment is a memory; `rules-of-hooks` is a gate. React rejects the mismatch at runtime
 *    with a message that names neither the file nor the cause.
 * 3. `@vitest/eslint-plugin`'s focus/disable rules. The Definition of Done currently asks a human to grep
 *    for `.only` and `.skip` (`docs/STATE.md` §6's hygiene scan). A test that reaches `main` disabled is
 *    worse than no test at all, because the run still reports green — and `100 passed` is a number this
 *    repository quotes in evidence lines everywhere.
 */
export default tseslint.config(
  {
    // `.promptkit/` is a vendored submodule (see `.gitmodules`) and `.kilo/`, `.fallow/` are IDE state.
    // Linting a third party's files produces findings with no owner to fix them.
    ignores: ['dist/', 'node_modules/', '.promptkit/**', '.kilo/**', '.fallow/**'],
  },

  js.configs.recommended,

  // ---- application and test source: type-checked, so the parser can see real types ------------------
  // `projectService` reads `tsconfig.app.json`, whose `include` is `["src"]`. Config files at the repo
  // root are therefore linted **without** type information below: asking for types on a file that no
  // tsconfig claims is an error, not a fallback.
  ...tseslint.configs.recommendedTypeChecked,
  {
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: {
      globals: { ...globals.browser, ...globals.es2024 },
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    plugins: { 'react-hooks': reactHooks },
    rules: {
      'react-hooks/rules-of-hooks': 'error',
      // Warning, not error — deliberately. `exhaustive-deps` is the only rule here that can be satisfied
      // either correctly or by silencing it with a ref, and a learner who reaches for the ref to silence a
      // build failure learns the wrong thing. It still fails CI, because `lint` runs `--max-warnings 0`.
      'react-hooks/exhaustive-deps': 'warn',
      'no-console': ['error', { allow: ['warn', 'error'] }],
      // A floating promise in a repository that models writes as async Results is how a refused write
      // becomes invisible: nothing awaits it, so nothing observes the `ok: false`.
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],

      // ---- two rules switched OFF, and the reasons are load-bearing -------------------------------
      // The repository seam is async **on purpose** even where localStorage is synchronous, because the
      // shape was chosen for M3's HTTP client (M2a invariant: "the swap changes one implementation file
      // and no call site"). `require-await` punishes exactly that: every `async list() { return err(...) }`
      // stub and every unavailable-storage backstop. The dangerous half of the same problem stays
      // covered — `no-floating-promises` is still an error, so an awaited-less write is caught.
      '@typescript-eslint/require-await': 'off',
      // All seven findings this rule produced were React-context destructuring:
      // `const { deleteApplication } = useApplications()`. The context value is an object literal whose
      // members are `useCallback(() => …)` arrows, so no `this` exists to lose — the rule is aimed at
      // class instances. Silencing it costs nothing; "fixing" the findings would mean rewriting every
      // consumer to call `api.deleteApplication(...)` for zero behavioural gain.
      '@typescript-eslint/unbound-method': 'off',
    },
  },

  // ---- tests: vitest globals are injected by the runner, not imported -------------------------------
  {
    files: ['src/**/*.test.{ts,tsx}', 'src/test/**/*.{ts,tsx}'],
    languageOptions: { globals: { ...globals.vitest } },
    plugins: { 'testing-library': testingLibrary, vitest },
    rules: {
      // `await` a `findBy*` or wrap the assertion in `waitFor`, or the test can pass before the
      // component has re-rendered. This is the async contract the whole suite is built on, and the one
      // a reader of `reload.test.tsx` has to trust without a gate.
      'testing-library/await-async-queries': 'error',
      'testing-library/no-debugging-utils': 'error',
      'testing-library/prefer-presence-queries': 'error',
      // Deliberately off: `crossTabSync.test.tsx` reads `card.querySelector('h3')` on purpose, to prove
      // *which card* carries a title rather than that a title exists somewhere. AGENTS.md's
      // behaviour-focused rule is "assert what a user can see", and that assertion needs the node.
      'testing-library/no-node-access': 'off',
      'vitest/no-focused-tests': 'error',
      'vitest/no-disabled-tests': 'error',
      'vitest/no-commented-out-tests': 'error',
      // Two `it()` blocks with the same name in one file: one of them is silently unreachable.
      'vitest/no-identical-title': 'error',
    },
  },

  // ---- repo-root tooling: plain JS, Node globals, no type information -------------------------------
  {
    files: ['*.config.{js,ts}', '.stylelintrc*.js'],
    languageOptions: { globals: { ...globals.node }, parserOptions: { projectService: false } },
    extends: [tseslint.configs.disableTypeChecked],
  },
)
