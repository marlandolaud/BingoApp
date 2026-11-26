#!/bin/sh
# Container entrypoint: build the basic-auth policy, then tunnel to the UI proxy.
set -eu
sh /ngrok/render-policy.sh > /tmp/policy.json
set -- http "${NGROK_UPSTREAM:-ui:80}" --traffic-policy-file /tmp/policy.json --log stdout
[ -n "${NGROK_URL:-}" ] && set -- "$@" --url "$NGROK_URL"
exec ngrok --config /var/lib/ngrok/ngrok.yml "$@"
