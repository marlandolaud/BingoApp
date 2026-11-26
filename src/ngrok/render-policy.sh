#!/bin/sh
# Prints an ngrok traffic policy (JSON) that puts HTTP basic auth in front of the
# tunnel, using BASIC_AUTH_USER / BASIC_AUTH_PASS. Exits non-zero on bad input so
# the tunnel never starts unprotected.
set -eu

fail() { echo "render-policy: $*" >&2; exit 1; }

user="${BASIC_AUTH_USER:-}"
pass="${BASIC_AUTH_PASS:-}"

[ -n "$user" ] || fail "BASIC_AUTH_USER is not set"
[ -n "$pass" ] || fail "BASIC_AUTH_PASS is not set"
case "$user" in *:*) fail "BASIC_AUTH_USER must not contain a colon" ;; esac
[ "${#pass}" -ge 8 ] || fail "BASIC_AUTH_PASS must be at least 8 characters (ngrok requirement)"

json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }

printf '{"on_http_request":[{"actions":[{"type":"basic-auth","config":{"credentials":["%s:%s"]}}]}]}\n' \
  "$(json_escape "$user")" "$(json_escape "$pass")"
