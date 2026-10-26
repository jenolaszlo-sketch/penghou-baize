$ErrorActionPreference = 'Stop'

$evidenceRoot = $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $evidenceRoot '..\..\..'))
$planDirectory = Join-Path $evidenceRoot 'candidate-plan'
$plan = Get-Content -LiteralPath (Join-Path $planDirectory 'plan.json') -Raw | ConvertFrom-Json -AsHashtable
$planIdentity = Get-Content -LiteralPath (Join-Path $planDirectory 'identity.json') -Raw | ConvertFrom-Json -AsHashtable
$build = $plan.source.build
$binding = Get-Content -LiteralPath (Join-Path $evidenceRoot 'artifact-binding.json') -Raw | ConvertFrom-Json -AsHashtable
$closure = Get-Content -LiteralPath (Join-Path $evidenceRoot 'deployment-closure-manifest.json') -Raw | ConvertFrom-Json -AsHashtable

function ConvertTo-CanonicalJson([AllowNull()][object]$value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [string]) { return ConvertTo-Json -InputObject $value -Compress }
    if ($value -is [bool]) { return $value.ToString().ToLowerInvariant() }
    if ($value -is [System.Collections.IDictionary]) {
        $keys = [string[]]@($value.Keys)
        [System.Array]::Sort($keys, [System.StringComparer]::Ordinal)
        $properties = foreach ($key in $keys) {
            $name = ConvertTo-Json -InputObject $key -Compress
            $encoded = ConvertTo-CanonicalJson $value[$key]
            "$name`:$encoded"
        }
        return '{' + ($properties -join ',') + '}'
    }
    if ($value -is [System.Collections.IList] -and $value -isnot [string]) {
        $items = foreach ($item in $value) { ConvertTo-CanonicalJson $item }
        return '[' + ($items -join ',') + ']'
    }
    if ($value -is [ValueType]) {
        $number = [Convert]::ToDecimal($value, [Globalization.CultureInfo]::InvariantCulture)
        return $number.ToString('0.#############################', [Globalization.CultureInfo]::InvariantCulture)
    }
    throw "unsupported-canonical-value:$($value.GetType().FullName)"
}

function Get-CanonicalJson([object]$value) { return ConvertTo-CanonicalJson $value }

function Get-CanonicalDigest([object]$value) {
    $json = Get-CanonicalJson $value
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
    return 'sha256:' + [Convert]::ToHexStringLower([System.Security.Cryptography.SHA256]::HashData($bytes))
}

# Confirm the PowerShell manifest writer reproduces the committed cross-platform vectors.
$vectors = Get-Content -LiteralPath (Join-Path $repoRoot 'eng\qualification\stage4b\canonical-evidence-vectors.json') -Raw |
    ConvertFrom-Json -AsHashtable
foreach ($vector in $vectors.vectors) {
    foreach ($input in $vector.inputs) {
        $value = ConvertFrom-Json -InputObject $input -AsHashtable
        $canonicalMatches = (Get-CanonicalJson $value) -ceq $vector.canonicalUtf8
        $digestMatches = (Get-CanonicalDigest $value) -ceq $vector.sha256
        if (-not $canonicalMatches -or -not $digestMatches) {
            throw "canonical-vector-mismatch:$($vector.name)"
        }
    }
}

$planHash = $planIdentity.planHash
if ($plan.kind -ne 'qualification-plan' -or $planIdentity.kind -ne 'qualification-plan-identity' -or $planIdentity.executionAuthorized -ne $false) { throw 'candidate-plan-identity-invalid' }
$case = $plan.cases[0]
$subject = @($plan.subjects | Where-Object id -eq $case.subjectId)[0]
$requested = $subject.binding.requested
$resolved = $subject.binding.resolved
$transport = $resolved.transport
$runnerPath = Join-Path $repoRoot 'eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll'
$runnerHash = 'sha256:' + (Get-FileHash -LiteralPath $runnerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$reservation = $case.requestedReservation
$observedContractIds = @($case.observations | ForEach-Object { $_.id })
$trialEntries = foreach ($plannedCase in $plan.cases) {
    foreach ($trial in $plannedCase.trials) {
        [ordered]@{
            caseId = $plannedCase.id
            trialId = $trial.id
            trialSlot = $trial.slot
            modelId = $plannedCase.modelId
            scenarioId = $plannedCase.scenario
            observedContractIds = @($plannedCase.observations | ForEach-Object { $_.id })
            maximumSemanticInvocations = $plannedCase.maximumSemanticInvocationsPerSlot
            maximumNeutralHttpCalls = $plannedCase.maximumNeutralHttpCallsPerSlot
            maximumPhysicalHttpAttempts = $plannedCase.maximumPhysicalWireAttemptsPerSlot
        }
    }
}

$proposal = [ordered]@{
    kind = 'qualification-live-run-proposal'
    schemaVersion = '2.0'
    proposalStatus = 'pending-independent-review-and-owner-acceptance'
    executionAuthorized = $false
    qualificationPerformed = $false
    authorizationIssued = $false
    note = 'This is a new R1 proposal. It is not a live authorization and cannot execute. Independent review and owner acceptance are required before a separate short-lived authorization is signed.'
    build = [ordered]@{
        sourceRevision = $build.sourceRevision
        buildRevision = $build.buildRevision
        productVersion = $build.productVersion
        configuration = $build.configuration
        targetFramework = $closure.targetFramework
        framework = $build.framework
        runtimeIdentifier = $build.runtimeIdentifier
        runnerRelativePath = $closure.runnerPath
        runnerSizeBytes = (Get-Item -LiteralPath $runnerPath).Length
        runnerSha256 = $runnerHash
        closureSchemaVersion = $closure.schemaVersion
        closureFileCount = $closure.files.Count
        closureManifestDigest = $closure.digest
    }
    plan = [ordered]@{
        planHash = $planHash
        contractRegistryHash = $plan.source.contractRegistryHash
        inventoryHash = $plan.source.inventoryHash
        policyHash = $plan.source.policyHash
        canonicalWriter = $plan.canonicalWriter
        liveEligibilityAtPlanning = $plan.liveEligibility
        planningBlockers = @($plan.liveEligibilityBlockers)
    }
    subject = [ordered]@{
        subjectId = $subject.id
        modelId = $subject.modelId
        hostingProvider = $requested.service
        provider = $resolved.provider
        endpointId = $resolved.endpointId
        endpointBaseUrl = $resolved.baseUrl
        endpointOrigin = $resolved.origin
        providerModel = $resolved.providerModel
        protocol = $requested.protocol
        apiVersion = $requested.apiVersion
        requestedRevision = $requested.revision
        mutableAlias = $requested.mutableAlias
        settings = $resolved.settings
    }
    transport = [ordered]@{
        profileId = $transport.profileId
        profileIdentity = $transport.identity
        protocolVersion = $transport.protocolVersion
        physicalAttemptUpperBound = $transport.physicalAttemptUpperBound
        status = $transport.state
        automaticRetries = $transport.handlerSettings.retryMiddleware
        redirects = $transport.handlerSettings.allowAutoRedirect
        proxy = $transport.handlerSettings.useProxy
        cookies = $transport.handlerSettings.useCookies
        decompression = $transport.handlerSettings.automaticDecompression
        authenticationNegotiation = $transport.handlerSettings.automaticAuthentication
    }
    tests = [ordered]@{
        selectedContracts = @($plan.selectedContracts | ForEach-Object { $_.id })
        cases = @($trialEntries)
        expectedMaximumDispatches = $case.maximumNeutralHttpCallsPerSlot
        streamingObservation = 'chat.streaming-integrity@2 observes the chat.baseline@3 invocation; it adds no request.'
    }
    budget = [ordered]@{
        maximumModelCalls = $plan.policy.maximumModelCalls
        maximumHttpRequests = $plan.policy.maximumHttpRequests
        maximumInputTokens = $plan.policy.maximumInputTokens
        maximumOutputTokens = $plan.policy.maximumOutputTokens
        maximumReasoningTokens = $plan.policy.maximumReasoningTokens
        maximumTotalTokens = $plan.policy.maximumTotalTokens
        maximumCost = $reservation.maximumCost
        currency = $reservation.currency
        maximumEstimatedLiability = "$($reservation.maximumCost) $($reservation.currency)"
        pricingSourceRef = $reservation.sourceRef
        allowanceVerified = $requested.allowanceVerified
        allowanceRef = $requested.allowanceRef
    }
    credentialRef = $requested.endpoint.apiKeySecretName
    requiredCredentialReadiness = 'Nonempty credential must be available through the configured provider before consumption; the value is not included in this proposal.'
    artifactDestination = 'artifacts/qualification/stage4b-openrouter-llama31-8b-smoke-r1'
    retainedRestrictions = @(
        'One smoke invocation only; a pass does not qualify beyond the selected contracts.',
        'No native tool calling, JSON Schema, reasoning-mode or multi-turn expansion.',
        'The provider model alias is mutable and the requested revision is unknown.',
        'HTTP/2, HTTP/3, proxy, redirect, authentication negotiation and custom-handler paths remain unsupported.',
        'Provider routing, billing semantics and provider-side execution counts remain outside Baize control.',
        'No automatic retry or model/endpoint substitution is authorized.'
    )
}

$proposalPath = Join-Path $evidenceRoot 'proposal-manifest.json'
$proposalJson = Get-CanonicalJson $proposal
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($proposalPath, $proposalJson, $utf8)
$proposalDigest = Get-CanonicalDigest $proposal

$checks = [ordered]@{
    canonicalVectorsMatched = $true
    planIdentityMatched = ($planHash -eq $planIdentity.planHash)
    sourceRevisionMatched = ($proposal.build.sourceRevision -eq $build.sourceRevision)
    closureDigestMatched = ($proposal.build.closureManifestDigest -eq $closure.digest)
    runnerHashMatched = ($proposal.build.runnerSha256 -eq $binding.runner.sha256)
    planScopeMatched = ($proposal.tests.cases.Count -eq $plan.cases.Count)
    physicalAttemptBoundIsOne = ($proposal.transport.physicalAttemptUpperBound -eq 1)
    financialCeilingUnchanged = ($proposal.budget.maximumCost -eq '0.0004' -and $proposal.budget.currency -eq 'USD')
    noAuthorizationIssued = ($proposal.authorizationIssued -eq $false)
    executionUnauthorized = ($proposal.executionAuthorized -eq $false)
}
if ($checks.Values -contains $false) { throw 'proposal-validation-failed' }

$validation = [ordered]@{
    kind = 'qualification-live-run-proposal-validation'
    schemaVersion = '1.0'
    validationStatus = 'valid-for-independent-review-only'
    proposalDigest = $proposalDigest
    planHash = $planHash
    sourceRevision = $build.sourceRevision
    checks = $checks
    executionAuthorized = $false
    qualificationPerformed = $false
    authorizationIssued = $false
}
[System.IO.File]::WriteAllText((Join-Path $evidenceRoot 'proposal-validation.json'),
    (Get-CanonicalJson $validation), $utf8)
[System.IO.File]::WriteAllText((Join-Path $evidenceRoot 'proposal-digest.txt'), "$proposalDigest`n", $utf8)
Write-Output (Get-CanonicalJson $validation)
