param(
    [string]$ProposalUrl = "http://localhost:5101",
    [string]$ContractUrl = "http://localhost:5102"
)

$ErrorActionPreference = "Stop"

Write-Host "=== InsurancePlatform Smoke Test (PowerShell) ===" -ForegroundColor Cyan

# Step 1: Wait for readiness of both services
Write-Host "1. Checking readiness..." -ForegroundColor Yellow
$ready = $false
for ($i = 1; $i -le 30; $i++) {
    try {
        $pRes = Invoke-WebRequest -Uri "$ProposalUrl/health/ready" -UseBasicParsing -TimeoutSec 2
        $cRes = Invoke-WebRequest -Uri "$ContractUrl/health/ready" -UseBasicParsing -TimeoutSec 2
        if ($pRes.StatusCode -eq 200 -and $cRes.StatusCode -eq 200) {
            $ready = $true
            Write-Host "   Both services are ready!" -ForegroundColor Green
            break
        }
    } catch {
        # ignore and wait
    }
    Write-Host "   Waiting for services ($i/30)..."
    Start-Sleep -Seconds 2
}

if (-not $ready) {
    Write-Error "Services failed to become ready in time."
    exit 1
}

# Step 2: Create proposal; expect 201 & under_review
Write-Host "2. Creating proposal..." -ForegroundColor Yellow
$proposalPayload = @{
    customerId = "CUST-SMOKE-1"
    productCode = "AUTO_SMOKE"
    insuredAmount = 60000.00
    monthlyPremium = 150.00
} | ConvertTo-Json

$createResponse = Invoke-WebRequest -Uri "$ProposalUrl/api/v1/proposals" -Method Post -Body $proposalPayload -ContentType "application/json"
if ($createResponse.StatusCode -ne 201) {
    Write-Error "Expected 201, got $($createResponse.StatusCode)"
    exit 1
}

$createdProposal = $createResponse.Content | ConvertFrom-Json
$proposalId = $createdProposal.id
if ($createdProposal.status -ne "under_review") {
    Write-Error "Expected under_review, got $($createdProposal.status)"
    exit 1
}
Write-Host "   Proposal created: ID=$proposalId, Status=$($createdProposal.status)" -ForegroundColor Green

function Get-ErrorBody($err) {
    try {
        $stream = $err.Exception.Response.GetResponseStream()
        if ($stream) {
            $reader = New-Object System.IO.StreamReader($stream)
            return $reader.ReadToEnd()
        }
    } catch {}
    return $err.ErrorDetails.Message
}

# Step 3: Try to contract; expect 409 proposal_not_approved
Write-Host "3. Attempting to contract proposal in under_review status..." -ForegroundColor Yellow
try {
    $contractPayload = @{ proposalId = $proposalId } | ConvertTo-Json
    Invoke-WebRequest -Uri "$ContractUrl/api/v1/contracts" -Method Post -Body $contractPayload -ContentType "application/json"
    Write-Error "Expected 409, but request succeeded!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $body = Get-ErrorBody $_
    if ($statusCode -ne 409 -or -not ($body -match "proposal_not_approved")) {
        Write-Error "Expected 409 proposal_not_approved, got ${statusCode}: ${body}"
        exit 1
    }
    Write-Host "   Correctly rejected with 409 proposal_not_approved" -ForegroundColor Green
}

# Step 4: Approve proposal; expect 200 & approved
Write-Host "4. Approving proposal..." -ForegroundColor Yellow
$approvePayload = @{ status = "approved" } | ConvertTo-Json
$approveResponse = Invoke-WebRequest -Uri "$ProposalUrl/api/v1/proposals/$proposalId/status" -Method Patch -Body $approvePayload -ContentType "application/json"
if ($approveResponse.StatusCode -ne 200) {
    Write-Error "Expected 200, got $($approveResponse.StatusCode)"
    exit 1
}
$approvedProposal = $approveResponse.Content | ConvertFrom-Json
if ($approvedProposal.status -ne "approved") {
    Write-Error "Expected approved, got $($approvedProposal.status)"
    exit 1
}
Write-Host "   Proposal approved: Status=$($approvedProposal.status)" -ForegroundColor Green

# Step 5: Contract approved proposal; expect 201 & Location & UTC date
Write-Host "5. Contracting approved proposal..." -ForegroundColor Yellow
$contractPayload = @{ proposalId = $proposalId } | ConvertTo-Json
$contractResponse = Invoke-WebRequest -Uri "$ContractUrl/api/v1/contracts" -Method Post -Body $contractPayload -ContentType "application/json"
if ($contractResponse.StatusCode -ne 201) {
    Write-Error "Expected 201, got $($contractResponse.StatusCode)"
    exit 1
}
$createdContract = $contractResponse.Content | ConvertFrom-Json
$contractId = $createdContract.id
Write-Host "   Contract created: ID=$contractId, Date=$($createdContract.contractedAtUtc)" -ForegroundColor Green

# Step 6: Query contract by ID and by proposal
Write-Host "6. Querying contract by ID and by proposal..." -ForegroundColor Yellow
$getById = Invoke-RestMethod -Uri "$ContractUrl/api/v1/contracts/$contractId" -Method Get
$getByProp = Invoke-RestMethod -Uri "$ContractUrl/api/v1/contracts/by-proposal/$proposalId" -Method Get
if ($getById.id -ne $contractId -or $getByProp.id -ne $contractId) {
    Write-Error "Contract query mismatch."
    exit 1
}
Write-Host "   Contract queries succeeded!" -ForegroundColor Green

# Step 7: Try to contract again; expect 409 contract_already_exists
Write-Host "7. Attempting duplicate contracting for same proposal..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri "$ContractUrl/api/v1/contracts" -Method Post -Body $contractPayload -ContentType "application/json"
    Write-Error "Expected 409, but request succeeded!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $body = Get-ErrorBody $_
    if ($statusCode -ne 409 -or -not ($body -match "contract_already_exists")) {
        Write-Error "Expected 409 contract_already_exists, got ${statusCode}: ${body}"
        exit 1
    }
    Write-Host "   Correctly rejected with 409 contract_already_exists" -ForegroundColor Green
}

# Step 8: Create and reject another proposal; verify it cannot be contracted
Write-Host "8. Creating and rejecting a second proposal, then verifying rejection of contract..." -ForegroundColor Yellow
$p2Payload = @{
    customerId = "CUST-SMOKE-2"
    productCode = "LIFE_SMOKE"
    insuredAmount = 100000.00
    monthlyPremium = 200.00
} | ConvertTo-Json

$p2Res = (Invoke-RestMethod -Uri "$ProposalUrl/api/v1/proposals" -Method Post -Body $p2Payload -ContentType "application/json").id
$rejectPayload = @{ status = "rejected" } | ConvertTo-Json
$null = Invoke-RestMethod -Uri "$ProposalUrl/api/v1/proposals/$p2Res/status" -Method Patch -Body $rejectPayload -ContentType "application/json"

try {
    $p2ContractPayload = @{ proposalId = $p2Res } | ConvertTo-Json
    Invoke-WebRequest -Uri "$ContractUrl/api/v1/contracts" -Method Post -Body $p2ContractPayload -ContentType "application/json"
    Write-Error "Expected 409, but request succeeded!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $body = Get-ErrorBody $_
    if ($statusCode -ne 409 -or -not ($body -match "proposal_not_approved")) {
        Write-Error "Expected 409 proposal_not_approved, got ${statusCode}: ${body}"
        exit 1
    }
    Write-Host "   Correctly rejected with 409 proposal_not_approved" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== ALL 8 SMOKE TEST STEPS PASSED SUCCESSFULLY ===" -ForegroundColor Green
