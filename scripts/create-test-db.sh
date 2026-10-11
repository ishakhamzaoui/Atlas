#!/usr/bin/env bash
# Creates a DEDICATED PostgreSQL role + database for the automated tests (never your dev/prod database).
# Run on the VM as a user that can `sudo -u postgres psql`.
#
#   ./scripts/create-test-db.sh                 # random password, prints the export line
#   ATLAS_TEST_DB_PASSWORD='...' ./scripts/create-test-db.sh
#
# Options (env): ATLAS_TEST_DB_NAME (default atlas_test), ATLAS_TEST_DB_USER (default atlas_test),
#                ATLAS_TEST_DB_HOST (default localhost), ATLAS_TEST_DB_PORT (default 5432).
set -euo pipefail

DB_NAME="${ATLAS_TEST_DB_NAME:-atlas_test}"
DB_USER="${ATLAS_TEST_DB_USER:-atlas_test}"
DB_HOST="${ATLAS_TEST_DB_HOST:-localhost}"
DB_PORT="${ATLAS_TEST_DB_PORT:-5432}"
DB_PASSWORD="${ATLAS_TEST_DB_PASSWORD:-pswd}"

case "$DB_NAME" in
  *test*) ;;
  *) echo "Refusing: database name must contain 'test' (got '$DB_NAME')." >&2; exit 1 ;;
esac

psql_admin() { sudo -u postgres psql -v ON_ERROR_STOP=1 -X "$@"; }

psql_admin <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${DB_USER}') THEN
    CREATE ROLE "${DB_USER}" LOGIN PASSWORD '${DB_PASSWORD}';
  ELSE
    ALTER ROLE "${DB_USER}" WITH LOGIN PASSWORD '${DB_PASSWORD}';
  END IF;
END
\$\$;
SQL

if ! psql_admin -tAc "SELECT 1 FROM pg_database WHERE datname='${DB_NAME}'" | grep -q 1; then
  psql_admin -c "CREATE DATABASE \"${DB_NAME}\" OWNER \"${DB_USER}\""
fi

echo
echo "Test database ready. Add this to your shell profile (do NOT commit it):"
echo "  export ATLAS_TEST_DB='Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD}'"
