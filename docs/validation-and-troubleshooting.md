# Validation and troubleshooting

Baize separates validation stages so failures remain actionable:

- `LlmConfigurationException` with `Structural` means the model, endpoint, profile, strategy, or named-route graph is inconsistent.
- `LlmConfigurationException` with `EndpointInitialization` means startup validation could not construct one or more endpoints or resolve their secrets. `EndpointFailures` contains safe endpoint details.
- `LlmRoutingException` means no route, model, registered endpoint, or compatible endpoint could be selected. It includes the target, configured model chain, and candidate outcomes.
- `LlmRequestValidationException` means the selected client cannot represent a requested feature.
- `LlmClientException` means provider transport or protocol execution failed.

Use `ExplainModelAsync`, `ExplainStrategyAsync`, or `ExplainRouteAsync` to inspect capability rejection, rank, cooldown, and the selected endpoint without inference. Enable `Penghou.Baize.Diagnostics` only while troubleshooting raw HTTP; it is off by default and must be protected because payloads can contain sensitive prompt content.
