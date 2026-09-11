/**
 * Stylelint configuration — DEBT-03 / DEBT-15.
 *
 * This file exists because of one measured fact: three orphan declarations (`margin-top: 10px`,
 * `padding: 8px 12px`, and a stray `}`) were written into `src/styles.css` by a scripted patch in
 * `f1ae7af` and survived **21 commits, a typecheck, a production build, and a suite that grew to 100
 * tests**. Nothing in this repository imports CSS in a way that could notice. `tsc` does not read CSS,
 * and Vite does not fail on it. The Red that justifies this config is not a test file — it is
 * `git show f1ae7af:src/styles.css`, which must fail lint. If that command ever passes, this config has
 * stopped doing its job and should be fixed rather than kept.
 *
 * `standard` is the config, not `standard-scss` (no preprocessor here) and not a hand-picked rule set:
 * the value of the shared config is that a newcomer has seen it before.
 */
export default {
  extends: ['stylelint-config-standard'],
  rules: {
    // The rule that catches DEBT-15's actual bug: a declaration that is not inside a rule block.
    // Introduced in stylelint 16.10 precisely for this class, and the one finding worth an error.
    'no-invalid-position-declaration': true,

    // Unclosed blocks and unbalanced braces are parse errors, which stylelint already reports.
    // What standard config flags that this repo does *not* want as noise:
    // Hex colours are hardcoded throughout, and DESIGN.md §8 keeps them that way until `pk:design`
    // extracts tokens (DEBT-08). Turning this on would demand a token system as a side effect of a
    // lint commit, which is exactly the unrelated refactor AGENTS.md forbids.
    'color-function-notation': null,
    'alpha-value-notation': null,
    'color-hex-length': null,
    // `!important` is used deliberately in one place for a UA-styled control; leave the judgement
    // to review rather than to a rule that cannot see why.
    'declaration-no-important': null,
    // DESIGN.md §2 records the measured palette in `rgba(…)` notation, and it is the document a human
    // reads. Rewriting eight call sites to `rgb(… / …)` would put the CSS out of agreement with the
    // design doc as a side effect of adding a linter — the notation rule yields to the authority.
    'color-function-alias-notation': null,
    // Selector specificity and `media-feature-name` notation are taste, not correctness. Style
    // decisions belong in DESIGN.md, which is the document a human reads.
    'no-descending-specificity': null,
    'media-feature-range-notation': null,
    'selector-class-pattern': null,
    'custom-property-pattern': null,
    'keyframes-name-pattern': null,
  },
}
