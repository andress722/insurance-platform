#!/usr/bin/env bash
set -euo pipefail

PROPOSAL_URL="${PROPOSAL_URL:-http://localhost:5101}"
CONTRACT_URL="${CONTRACT_URL:-http://localhost:5102}"

echo "=== InsurancePlatform Smoke Test ==="

# Step 1: Wait for readiness of both services
echo "1. Checking readiness..."
for i in {1..30}; do
  if curl -sf "${PROPOSAL_URL}/health/ready" > /dev/null && curl -sf "${CONTRACT_URL}/health/ready" > /dev/null; then
    echo "   Both services are ready!"
    break
  fi
  echo "   Waiting for services to become ready ($i/30)..."
  sleep 2
  if [ "$i" -eq 30 ]; then
    echo "ERROR: Services failed to become ready in time." >&2
    exit 1
  fi
done

# Step 2: Create proposal; expect 201 & under_review
echo "2. Creating proposal..."
CREATE_RESP=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST "${PROPOSAL_URL}/api/v1/proposals" \
  -H "Content-Type: application/json" \
  -d '{"customerId":"CUST-SMOKE-1","productCode":"AUTO_SMOKE","insuredAmount":60000.00,"monthlyPremium":150.00}')

STATUS_CODE=$(echo "$CREATE_RESP" | grep "HTTP_STATUS" | cut -d':' -f2)
BODY=$(echo "$CREATE_RESP" | grep -v "HTTP_STATUS")

if [ "$STATUS_CODE" -ne 201 ]; then
  echo "ERROR: Expected 201 on proposal creation, got $STATUS_CODE: $BODY" >&2
  exit 1
fi

PROPOSAL_ID=$(echo "$BODY" | grep -o '"id":"[^"]*' | cut -d'"' -f4)
PROPOSAL_STATUS=$(echo "$BODY" | grep -o '"status":"[^"]*' | cut -d'"' -f4)

if [ "$PROPOSAL_STATUS" != "under_review" ]; then
  echo "ERROR: Expected status 'under_review', got '$PROPOSAL_STATUS'" >&2
  exit 1
fi
echo "   Proposal created: ID=$PROPOSAL_ID, Status=$PROPOSAL_STATUS"

# Step 3: Try to contract; expect 409 proposal_not_approved
echo "3. Attempting to contract proposal in under_review status..."
CONTRACT_ATTEMPT=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST "${CONTRACT_URL}/api/v1/contracts" \
  -H "Content-Type: application/json" \
  -d "{\"proposalId\":\"$PROPOSAL_ID\"}")

STATUS_CODE=$(echo "$CONTRACT_ATTEMPT" | grep "HTTP_STATUS" | cut -d':' -f2)
BODY=$(echo "$CONTRACT_ATTEMPT" | grep -v "HTTP_STATUS")

if [ "$STATUS_CODE" -ne 409 ]; then
  echo "ERROR: Expected 409 when contracting under_review proposal, got $STATUS_CODE: $BODY" >&2
  exit 1
fi
if ! echo "$BODY" | grep -q "proposal_not_approved"; then
  echo "ERROR: Expected error code 'proposal_not_approved', got: $BODY" >&2
  exit 1
fi
echo "   Correctly rejected with 409 proposal_not_approved"

# Step 4: Approve proposal; expect 200 & approved
echo "4. Approving proposal..."
APPROVE_RESP=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X PATCH "${PROPOSAL_URL}/api/v1/proposals/${PROPOSAL_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"status":"approved"}')

STATUS_CODE=$(echo "$APPROVE_RESP" | grep "HTTP_STATUS" | cut -d':' -f2)
BODY=$(echo "$APPROVE_RESP" | grep -v "HTTP_STATUS")

if [ "$STATUS_CODE" -ne 200 ]; then
  echo "ERROR: Expected 200 on proposal approval, got $STATUS_CODE: $BODY" >&2
  exit 1
fi

NEW_STATUS=$(echo "$BODY" | grep -o '"status":"[^"]*' | cut -d'"' -f4)
if [ "$NEW_STATUS" != "approved" ]; then
  echo "ERROR: Expected status 'approved', got '$NEW_STATUS'" >&2
  exit 1
fi
echo "   Proposal approved: Status=$NEW_STATUS"

# Step 5: Contract approved proposal; expect 201 & Location & UTC date
echo "5. Contracting approved proposal..."
CONTRACT_RESP=$(curl -s -i -w "\nHTTP_STATUS:%{http_code}" -X POST "${CONTRACT_URL}/api/v1/contracts" \
  -H "Content-Type: application/json" \
  -d "{\"proposalId\":\"$PROPOSAL_ID\"}")

STATUS_CODE=$(echo "$CONTRACT_RESP" | grep "HTTP_STATUS" | cut -d':' -f2)

if [ "$STATUS_CODE" -ne 201 ]; then
  echo "ERROR: Expected 201 on contracting, got $STATUS_CODE: $CONTRACT_RESP" >&2
  exit 1
fi

CONTRACT_ID=$(echo "$CONTRACT_RESP" | grep -o '"id":"[^"]*' | cut -d'"' -f4)
CONTRACTED_AT=$(echo "$CONTRACT_RESP" | grep -o '"contractedAtUtc":"[^"]*' | cut -d'"' -f4)

if [ -z "$CONTRACT_ID" ]; then
  echo "ERROR: Contract ID is empty in response" >&2
  exit 1
fi
echo "   Contract created: ID=$CONTRACT_ID, ContractedAtUtc=$CONTRACTED_AT"

# Step 6: Query contract by ID and by proposal
echo "6. Querying contract by ID and by proposal..."
GET_BY_ID=$(curl -s -w "\nHTTP_STATUS:%{http_code}" "${CONTRACT_URL}/api/v1/contracts/${CONTRACT_ID}")
STATUS_CODE=$(echo "$GET_BY_ID" | grep "HTTP_STATUS" | cut -d':' -f2)
if [ "$STATUS_CODE" -ne 200 ]; then
  echo "ERROR: Expected 200 querying contract by ID, got $STATUS_CODE" >&2
  exit 1
fi

GET_BY_PROP=$(curl -s -w "\nHTTP_STATUS:%{http_code}" "${CONTRACT_URL}/api/v1/contracts/by-proposal/${PROPOSAL_ID}")
STATUS_CODE=$(echo "$GET_BY_PROP" | grep "HTTP_STATUS" | cut -d':' -f2)
if [ "$STATUS_CODE" -ne 200 ]; then
  echo "ERROR: Expected 200 querying contract by proposal, got $STATUS_CODE" >&2
  exit 1
fi
echo "   Contract queries succeeded!"

# Step 7: Try to contract again; expect 409 contract_already_exists
echo "7. Attempting duplicate contracting for same proposal..."
DUPLICATE_RESP=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST "${CONTRACT_URL}/api/v1/contracts" \
  -H "Content-Type: application/json" \
  -d "{\"proposalId\":\"$PROPOSAL_ID\"}")

STATUS_CODE=$(echo "$DUPLICATE_RESP" | grep "HTTP_STATUS" | cut -d':' -f2)
BODY=$(echo "$DUPLICATE_RESP" | grep -v "HTTP_STATUS")

if [ "$STATUS_CODE" -ne 409 ]; then
  echo "ERROR: Expected 409 on duplicate contract, got $STATUS_CODE: $BODY" >&2
  exit 1
fi
if ! echo "$BODY" | grep -q "contract_already_exists"; then
  echo "ERROR: Expected code 'contract_already_exists', got: $BODY" >&2
  exit 1
fi
echo "   Correctly rejected with 409 contract_already_exists"

# Step 8: Create and reject another proposal; verify it cannot be contracted
echo "8. Creating and rejecting a second proposal, then verifying rejection of contract..."
P2_RESP=$(curl -s -X POST "${PROPOSAL_URL}/api/v1/proposals" \
  -H "Content-Type: application/json" \
  -d '{"customerId":"CUST-SMOKE-2","productCode":"LIFE_SMOKE","insuredAmount":100000.00,"monthlyPremium":200.00}')

P2_ID=$(echo "$P2_RESP" | grep -o '"id":"[^"]*' | cut -d'"' -f4)

curl -s -f -X PATCH "${PROPOSAL_URL}/api/v1/proposals/${P2_ID}/status" \
  -H "Content-Type: application/json" \
  -d '{"status":"rejected"}' > /dev/null

P2_CONTRACT_RESP=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST "${CONTRACT_URL}/api/v1/contracts" \
  -H "Content-Type: application/json" \
  -d "{\"proposalId\":\"$P2_ID\"}")

STATUS_CODE=$(echo "$P2_CONTRACT_RESP" | grep "HTTP_STATUS" | cut -d':' -f2)
BODY=$(echo "$P2_CONTRACT_RESP" | grep -v "HTTP_STATUS")

if [ "$STATUS_CODE" -ne 409 ]; then
  echo "ERROR: Expected 409 when contracting rejected proposal, got $STATUS_CODE: $BODY" >&2
  exit 1
fi
if ! echo "$BODY" | grep -q "proposal_not_approved"; then
  echo "ERROR: Expected code 'proposal_not_approved', got: $BODY" >&2
  exit 1
fi
echo "   Correctly rejected with 409 proposal_not_approved"

echo ""
echo "=== ALL 8 SMOKE TEST STEPS PASSED SUCCESSFULLY ==="
