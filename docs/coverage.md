# Coverage policy

Coverage is a regression signal, not the definition of correctness. Tests should
exercise observable behavior, failure handling, capability boundaries, malformed
provider data, and lifecycle behavior. Avoid tests that merely execute a line
without asserting its contract.

## Baseline

The baseline below was measured per production package with Coverlet. Multi-targeted
projects use the lowest result across their target frameworks. The initial CI gate
is deliberately below the measured branch baseline so small compiler-generated
changes do not make the gate brittle.

| Package | Line | Branch | Initial CI floor |
|---|---:|---:|---:|
| `Penghou.Baize` | 66.31% | 59.04% | 55% |
| `Penghou.Baize.Claude` | 86.62% | 71.22% | 69% |
| `Penghou.Baize.OpenAi` | 90.25% | 76.72% | 74% |
| `Penghou.Baize.Ollama` | 91.74% | 79.06% | 77% |
| `Penghou.Baize.Gemini` | 87.32% | 67.57% | 65% |
| `Penghou.Baize.Router` | 82.14% | 73.86% | 72% |
| `Penghou.Baize.Batch` | 86.01% | 63.59% | 61% |
| `Penghou.Baize.Tools` | 86.25% | 67.24% | 65% |
| `Penghou.Baize.Extensions.AI` | 73.97% | 64.42% | 62% |
| `Penghou.Baize.Diagnostics` | 66.06% | 52.12% | 50% |

Each package is measured in isolation using a Coverlet `Include` filter. Its CI
floor applies to both line and branch coverage. The common floor is based on the
lower branch result, so line coverage currently has more headroom.

## Raising a floor

1. Inspect uncovered branches and identify missing behavior or risk.
2. Add tests that assert that behavior, including negative and boundary cases.
3. Remeasure every target framework for the affected package.
4. Raise the package floor conservatively, leaving a small margin below the new
   measured result.

Live provider integration tests are excluded from these deterministic gates. They
validate protocol compatibility separately because credentials, quota, model
availability, and provider behavior can change independently of the repository.
