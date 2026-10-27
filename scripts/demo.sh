#!/usr/bin/env bash
# A guided tour of the API against a running instance.
#   docker compose up --build
#   ./scripts/demo.sh
# Needs curl and jq. Set OPS_KEY to the configured Operations__ApiKey to include the reconciliation report.
set -euo pipefail

API="${API_URL:-http://localhost:8080}/api"
JSON='Content-Type: application/json'
PASSWORD='demo-password-123'
RUN="$(date +%s)"

if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then bold=$'\e[1m'; dim=$'\e[2m'; green=$'\e[32m'; red=$'\e[31m'; reset=$'\e[0m'; else bold=''; dim=''; green=''; red=''; reset=''; fi

step() { printf '\n%s%s%s\n' "$bold" "$1" "$reset"; }
say() { printf '  %s\n' "$1"; }
new_key() { cat /proc/sys/kernel/random/uuid 2>/dev/null || uuidgen; }

register_and_login() {
  local email="$1.$RUN@example.com"
  curl -sf -X POST "$API/auth/register" -H "$JSON" \
    -d "{\"email\":\"$email\",\"fullName\":\"$2\",\"password\":\"$PASSWORD\"}" > /dev/null
  curl -sf -X POST "$API/auth/login" -H "$JSON" -d "{\"email\":\"$email\",\"password\":\"$PASSWORD\"}" | jq -r .accessToken
}

open_account() {
  curl -sf -X POST "$API/accounts" -H "$JSON" -H "Authorization: Bearer $1" -d "{\"currency\":\"$2\"}"
}

balance() {
  curl -sf "$API/accounts/$2" -H "Authorization: Bearer $1" | jq -r '"\(.balance) \(.currency)"'
}

transfer() { # token key source destination amount → "<status> <body>"
  curl -s -o /tmp/demo-body -w '%{http_code}' -X POST "$API/transfers" -H "$JSON" -H "Authorization: Bearer $1" \
    -H "Idempotency-Key: $2" -d "{\"sourceAccountId\":\"$3\",\"destinationAccountNumber\":\"$4\",\"amount\":$5}"
  printf ' %s\n' "$(cat /tmp/demo-body)"
}

step "1. Two customers sign up and log in"
ayse=$(register_and_login "ayse" "Ayşe Yılmaz")
mehmet=$(register_and_login "mehmet" "Mehmet Demir")
say "JWT for Ayşe:   ${ayse:0:24}…"
say "JWT for Mehmet: ${mehmet:0:24}…"

step "2. Each opens a TRY account; Ayşe deposits 1,000.00"
a=$(open_account "$ayse" TRY); a_id=$(jq -r .id <<< "$a")
m=$(open_account "$mehmet" TRY); m_number=$(jq -r .number <<< "$m"); m_id=$(jq -r .id <<< "$m")
curl -sf -X POST "$API/accounts/$a_id/deposits" -H "$JSON" -H "Authorization: Bearer $ayse" \
  -H "Idempotency-Key: $(new_key)" -d '{"amount":1000,"description":"Maaş"}' > /dev/null
say "Ayşe:   $(balance "$ayse" "$a_id")"
say "Mehmet: account $m_number"

step "3. Ayşe sends 250.00 to Mehmet's account number"
key=$(new_key)
result=$(transfer "$ayse" "$key" "$a_id" "$m_number" 250)
say "HTTP ${result%% *} → transaction $(jq -r .id <<< "${result#* }")"
say "Ayşe: $(balance "$ayse" "$a_id")   Mehmet: $(balance "$mehmet" "$m_id")"

step "4. Her app times out and retries with the same Idempotency-Key"
replayed=$(curl -s -D - -o /dev/null -X POST "$API/transfers" -H "$JSON" -H "Authorization: Bearer $ayse" -H "Idempotency-Key: $key" \
  -d "{\"sourceAccountId\":\"$a_id\",\"destinationAccountNumber\":\"$m_number\",\"amount\":250}" | grep -i '^idempotent-replayed' | tr -d '\r')
say "${replayed:-no replay header}  ${dim}(first response returned, nothing moved)${reset}"
say "Ayşe: $(balance "$ayse" "$a_id")   Mehmet: $(balance "$mehmet" "$m_id")"

step "5. 100 transfers of 15.00 hit Ayşe's 750.00 at the same moment"
seq 100 | xargs -P 100 -I{} sh -c "curl -s -o /dev/null -w '%{http_code}\n' -X POST '$API/transfers' -H '$JSON' \
  -H 'Authorization: Bearer $ayse' -H \"Idempotency-Key: \$(cat /proc/sys/kernel/random/uuid)\" \
  -d '{\"sourceAccountId\":\"$a_id\",\"destinationAccountNumber\":\"$m_number\",\"amount\":15}'" > /tmp/demo-codes
ok=$(grep -c '^201$' /tmp/demo-codes || true); rejected=$(grep -c '^422$' /tmp/demo-codes || true)
say "${green}${ok} succeeded${reset}, ${red}${rejected} rejected: insufficient_funds${reset}  ${dim}(750 / 15 = 50)${reset}"
say "Ayşe: $(balance "$ayse" "$a_id")   Mehmet: $(balance "$mehmet" "$m_id")"

step "6. Currencies never mix"
e=$(open_account "$mehmet" EUR); e_number=$(jq -r .number <<< "$e")
result=$(transfer "$mehmet" "$(new_key)" "$m_id" "$e_number" 5)
say "TRY → EUR: HTTP ${result%% *} $(jq -r .code <<< "${result#* }")"

step "7. Mehmet's statement, newest first"
curl -sf "$API/accounts/$m_id/transactions?limit=4" -H "Authorization: Bearer $mehmet" \
  | jq -r '.items[] | "  \(.type | .[0:10] | . + " " * (10 - length)) \(if .amount > 0 then "+" else "" end)\(.amount)  → \(.balanceAfter)"'

if [ -n "${OPS_KEY:-}" ]; then
  step "8. Reconciliation: every cached balance against the ledger"
  curl -sf "$API/ops/reconciliation" -H "X-Operations-Key: $OPS_KEY" \
    | jq -r '"  healthy: \(.healthy)   accounts: \(.customerAccounts)   unbalanced transactions: \(.unbalancedTransactions)   drifted accounts: \(.driftedAccounts | length)"'
fi
