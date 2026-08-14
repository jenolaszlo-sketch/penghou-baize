# Best-of-N sample

This sample demonstrates how generation, logical batching, and an
application-level selection policy compose without putting selection policy into
provider clients:

1. `IGenerationBatchExecutor` generates N candidates, splitting them into
   concurrent chunks bounded by the endpoint's native candidate limit.
2. An application-level scorer (here a deterministic heuristic; an evaluator LLM
   would fit in the same place) scores every candidate.
3. The best candidate is selected.

```powershell
dotnet run --project samples/Penghou.Baize.BestOfN
```

The sample uses an in-process deterministic image client so it runs with no
network and no API key. Swap in `AddBaizeOpenAiGeneration` or
`AddBaizeRunwayGeneration` and the same composition works against real
providers.
