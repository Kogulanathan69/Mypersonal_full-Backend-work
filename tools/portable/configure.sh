#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

STATE="${HOME}/.local/share/buildmind-portable"
SECRETS="${STATE}/secrets"

runtime_password="$(cat "$SECRETS/mysql_runtime_password")"
migrator_password="$(cat "$SECRETS/mysql_migrator_password")"
jwt_key="$(cat "$SECRETS/jwt_signing_key")"
python_token="$(cat "$SECRETS/python_internal_token")"
rag_token="$(cat "$SECRETS/rag_internal_token")"

cat > "$SECRETS/api.env" <<EOF
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://0.0.0.0:8080
Database__Enabled=true
ConnectionStrings__BuildMind=Server=mysql;Port=3306;Database=buildmind_dev;User ID=bm_runtime;Password=${runtime_password};SslMode=VerifyFull;SslCa=/run/bm/mysql/ca.pem;SslCert=/run/bm/mysql/client-cert.pem;SslKey=/run/bm/mysql/client-key.pem
Authentication__Enabled=true
Authentication__RefreshTokens__Enabled=true
Authentication__PasswordRecovery__Enabled=true
Jwt__Issuer=buildmind-portable
Jwt__Audience=buildmind-portable-api
Jwt__SigningKey=${jwt_key}
CaptainIntegration__Enabled=true
CaptainIntegration__RequirementCatalogueFile=/app/samples/requirement-catalogue.synthetic.json
CaptainIntegration__GenerationPolicyFile=/app/samples/generation-policy.synthetic.json
CaptainIntegration__ArchitectRecipientsFile=/app/samples/architect-directory.synthetic.json
CaptainIntegration__SurveyStorageDirectory=/var/lib/buildmind/surveys
CaptainIntegration__ReportStorageDirectory=/var/lib/buildmind/reports
CaptainIntegration__SharingPolicyVersion=captain-sharing-policy-dev-1
CaptainIntegration__SurveyWorker__PollSeconds=2
CaptainIntegration__SurveyWorker__LeaseSeconds=120
CaptainIntegration__AiEdit__Provider=rule-based-local
CaptainIntegration__Email__SmtpHost=mailpit
CaptainIntegration__Email__SmtpPort=1025
CaptainIntegration__Email__FromAddress=no-reply@buildmind.invalid
CaptainIntegration__Email__UseStartTls=true
CaptainIntegration__Email__AllowExternalDelivery=true
CaptainIntegration__PythonMtls__ServerCaFile=/run/bm/pki/server-ca.crt
CaptainIntegration__PythonMtls__ClientCertFile=/run/bm/pki/api-client.crt
CaptainIntegration__PythonMtls__ClientKeyFile=/run/bm/pki/api-client.key
Services__Python__Enabled=true
Services__Python__BaseUrl=https://python-services:8091
Services__Python__AuthToken=${python_token}
Services__Python__TimeoutSeconds=30
Services__Rag__Enabled=true
Services__Rag__BaseUrl=https://rag-service:8092
Services__Rag__AuthToken=${rag_token}
Regulations__Retrieval__Enabled=true
SSL_CERT_FILE=/run/bm/ca-bundle.pem
EOF

cat > "$SECRETS/migrator.env" <<EOF
BUILDMIND_MIGRATIONS_CONNECTION=Server=mysql;Port=3306;Database=buildmind_dev;User ID=bm_migrator;Password=${migrator_password};SslMode=VerifyFull;SslCa=/run/bm/mysql/ca.pem;SslCert=/run/bm/mysql/client-cert.pem;SslKey=/run/bm/mysql/client-key.pem
EOF

cat > "$SECRETS/rag.env" <<EOF
BUILDMIND_RAG_AUTH=required
BUILDMIND_RAG_INTERNAL_TOKENS=${rag_token}
EOF

mkdir -p "$STATE/corpus"
chmod 700 "$STATE/corpus"

chmod 600 "$SECRETS/api.env"
chmod 600 "$SECRETS/migrator.env"
chmod 600 "$SECRETS/rag.env"

echo "PORTABLE_API_CONFIGURATION_READY"
echo "PORTABLE_MIGRATION_CONFIGURATION_READY"
echo "PORTABLE_RAG_CONFIGURATION_READY"
echo "PORTABLE_SERVICE_CONFIGURATION_READY"
