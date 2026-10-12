#!/usr/bin/env bash
# =====================================================================================================================
# Atlas - development VM bootstrap for a FRESH Ubuntu Server (22.04 / 24.04 or newer).
#
# What it does (idempotent: safe to re-run at any time, e.g. after `git pull`, to re-sync, rebuild and retest):
#   1. installs git, openssl, PostgreSQL and the .NET 10 SDK (+ dotnet-ef), natively - no Docker, no containers
#   2. creates a DEV database (atlas) and a dedicated TEST database (atlas_test), each with its own role/password
#   3. clones https://github.com/ishakhamzaoui/Atlas.git, prepares git (identity, SSH key for pushing)
#   4. configures per-machine secrets with `dotnet user-secrets` (connection string, JWT key, TLS certificate)
#   5. creates a self-signed LAN TLS certificate and lets the app bind ports 80/443 without root
#   6. restores, builds, applies the committed EF migrations to the dev DB, and runs the whole test suite
#
# Run it as a normal user that has sudo (NOT as root):
#     curl -fsSLO https://raw.githubusercontent.com/ishakhamzaoui/Atlas/main/scripts/setup-dev-vm.sh
#     less setup-dev-vm.sh                      # review before running anything with sudo
#     GIT_USER_NAME="Your Name" GIT_USER_EMAIL="you@example.com" bash setup-dev-vm.sh
#
# Optional environment variables (all have sensible defaults):
#   ATLAS_DIR            checkout location                      (default: $HOME/Atlas)
#   ATLAS_BRANCH         branch to check out                    (default: main)
#   ATLAS_REPO_URL       HTTPS clone URL
#   ATLAS_REPO_SSH       SSH URL used for pushing
#   GIT_USER_NAME / GIT_USER_EMAIL   git identity, set globally only if not already configured
#   TLS_EXTRA_SANS       extra certificate names, e.g. "DNS:atlas-dev2.lan,IP:192.168.1.61"
#   SKIP_TLS=1           do not create the TLS certificate / configure Kestrel
#   SKIP_PORTS=1         do not change net.ipv4.ip_unprivileged_port_start (needed for ports 80/443 without root)
#   SKIP_TESTS=1         do not run the test suite
#   BOOTSTRAP_ADMIN=1    also create the first 'admin' account (prints a one-time temporary password)
#
# Secrets (database passwords, test connection string) are generated here, stored ONLY in
# $HOME/.config/atlas/dev.env (mode 600) and in dotnet user-secrets. Nothing secret goes into Git.
# Every run is logged to $HOME/atlas-setup.log.
# =====================================================================================================================
set -Eeuo pipefail

ATLAS_REPO_URL="${ATLAS_REPO_URL:-https://github.com/ishakhamzaoui/Atlas.git}"
ATLAS_REPO_SSH="${ATLAS_REPO_SSH:-git@github.com:ishakhamzaoui/Atlas.git}"
ATLAS_DIR="${ATLAS_DIR:-$HOME/Atlas}"
ATLAS_BRANCH="${ATLAS_BRANCH:-main}"
DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
CONFIG_DIR="${CONFIG_DIR:-$HOME/.config/atlas}"
ENV_FILE="$CONFIG_DIR/dev.env"
TLS_DIR="$CONFIG_DIR/tls"
LOG_FILE="${LOG_FILE:-$HOME/atlas-setup.log}"

DEV_DB="atlas";       DEV_ROLE="atlas"
TEST_DB="atlas_test"; TEST_ROLE="atlas_test"

SUMMARY=()

# ---------------------------------------------------------------------------------------------------------- helpers
log()  { printf '\n\033[1;34m==>\033[0m \033[1m%s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }
warn() { printf '\033[1;33mWARN:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }
note() { SUMMARY+=("$*"); }

random_hex() { openssl rand -hex "${1:-16}"; }

# Replace (or append) a block delimited by marker comments. Idempotent.
upsert_block() { # file marker content
  local file=$1 marker=$2 content=$3 begin end tmp
  begin="# >>> $marker >>>"; end="# <<< $marker <<<"
  touch "$file"
  tmp=$(mktemp)
  awk -v b="$begin" -v e="$end" '$0==b{skip=1} !skip{print} $0==e{skip=0}' "$file" > "$tmp"
  { cat "$tmp"; printf '%s\n%s\n%s\n' "$begin" "$content" "$end"; } > "$file"
  rm -f "$tmp"
}

psql_admin() { (cd / && sudo -u postgres psql -v ON_ERROR_STOP=1 -X -q "$@"); }

# ------------------------------------------------------------------------------------------------------------ steps
preflight() {
  log "Pre-flight checks"
  [[ ${EUID} -ne 0 ]] || die "Run as a normal user with sudo, not as root (the app and its secrets belong to your user)."
  command -v sudo >/dev/null || die "sudo is required."
  sudo -v || die "sudo authentication failed."

  # shellcheck disable=SC1091
  if [[ -r /etc/os-release ]]; then . /etc/os-release; fi
  [[ "${ID:-}" == "ubuntu" ]] || die "This script targets Ubuntu Server (found: ${PRETTY_NAME:-unknown})."
  info "OS: ${PRETTY_NAME}"

  getent hosts github.com >/dev/null || die "Cannot resolve github.com - check the VM's network/DNS."
}

install_packages() {
  log "Installing system packages (git, openssl, PostgreSQL)"
  sudo DEBIAN_FRONTEND=noninteractive apt-get update -y
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y \
    ca-certificates curl git openssl openssh-client unzip postgresql postgresql-contrib
}

have_dotnet_sdk() { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL%%.*}\."; }

install_dotnet() {
  log "Installing the .NET ${DOTNET_CHANNEL} SDK"
  export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

  if have_dotnet_sdk; then
    info "Already installed: $(dotnet --version)"
  else
    if apt-cache show "dotnet-sdk-${DOTNET_CHANNEL}" >/dev/null 2>&1 &&
       sudo DEBIAN_FRONTEND=noninteractive apt-get install -y "dotnet-sdk-${DOTNET_CHANNEL}"; then
      info "Installed from the Ubuntu package archive."
    else
      info "Not available via apt on this release - using Microsoft's official dotnet-install script."
      local installer; installer=$(mktemp)
      curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
      bash "$installer" --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet"
      rm -f "$installer"
    fi
  fi

  have_dotnet_sdk || die ".NET ${DOTNET_CHANNEL} SDK installation failed."
  info "dotnet $(dotnet --version)"

  # EF Core CLI, matching the SDK's major version
  if dotnet tool update --global dotnet-ef --version "${DOTNET_CHANNEL%%.*}.*" >/dev/null 2>&1 ||
     dotnet tool install --global dotnet-ef --version "${DOTNET_CHANNEL%%.*}.*" >/dev/null 2>&1; then
    info "dotnet-ef $(dotnet ef --version 2>/dev/null | tail -n1)"
  else
    die "Could not install dotnet-ef."
  fi

  # Persist PATH + telemetry opt-out + the test-DB variable for future shells (login and interactive).
  local block
  block=$'export DOTNET_CLI_TELEMETRY_OPTOUT=1\nexport DOTNET_NOLOGO=1\n'
  if [[ -d "$HOME/.dotnet" ]]; then block+=$'export DOTNET_ROOT="$HOME/.dotnet"\n'; fi
  block+=$'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"\n'
  block+=$'[ -f "$HOME/.config/atlas/dev.env" ] && . "$HOME/.config/atlas/dev.env"'
  upsert_block "$HOME/.profile" "atlas dev environment" "$block"
  upsert_block "$HOME/.bashrc"  "atlas dev environment" "$block"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
  note ".NET SDK $(dotnet --version) + dotnet-ef installed (PATH persisted in ~/.profile and ~/.bashrc)"
}

setup_postgres() {
  log "Setting up PostgreSQL (native) with dev and test databases"
  sudo systemctl enable --now postgresql
  local tries=0
  until pg_isready -q -h localhost; do
    tries=$((tries + 1))
    (( tries < 30 )) || die "PostgreSQL did not become ready."
    sleep 1
  done

  mkdir -p "$CONFIG_DIR"; chmod 700 "$CONFIG_DIR"

  # Reuse previously generated passwords so re-running does not break anything.
  local ATLAS_DEV_DB_PASSWORD="" ATLAS_TEST_DB_PASSWORD=""
  # shellcheck disable=SC1090
  if [[ -f "$ENV_FILE" ]]; then . "$ENV_FILE"; fi
  ATLAS_DEV_DB_PASSWORD="${ATLAS_DEV_DB_PASSWORD:-$(random_hex 16)}"
  ATLAS_TEST_DB_PASSWORD="${ATLAS_TEST_DB_PASSWORD:-$(random_hex 16)}"

  ensure_db "$DEV_DB"  "$DEV_ROLE"  "$ATLAS_DEV_DB_PASSWORD"
  ensure_db "$TEST_DB" "$TEST_ROLE" "$ATLAS_TEST_DB_PASSWORD"

  DEV_CONNECTION="Host=localhost;Port=5432;Database=${DEV_DB};Username=${DEV_ROLE};Password=${ATLAS_DEV_DB_PASSWORD}"
  TEST_CONNECTION="Host=localhost;Port=5432;Database=${TEST_DB};Username=${TEST_ROLE};Password=${ATLAS_TEST_DB_PASSWORD}"

  ( umask 077
    cat > "$ENV_FILE" <<ENVFILE
# Generated by setup-dev-vm.sh - machine-local secrets. NEVER commit or share this file.
ATLAS_DEV_DB_PASSWORD='${ATLAS_DEV_DB_PASSWORD}'
ATLAS_TEST_DB_PASSWORD='${ATLAS_TEST_DB_PASSWORD}'
# Used by the integration tests (dedicated native test database; every fixture uses its own throw-away schema):
export ATLAS_TEST_DB='${TEST_CONNECTION}'
ENVFILE
  )
  chmod 600 "$ENV_FILE"
  export ATLAS_TEST_DB="$TEST_CONNECTION"

  # Prove both logins work over TCP exactly as the application will use them.
  PGPASSWORD="$ATLAS_DEV_DB_PASSWORD"  psql -h localhost -U "$DEV_ROLE"  -d "$DEV_DB"  -Atqc 'select 1' >/dev/null || die "Cannot log in to the dev database."
  PGPASSWORD="$ATLAS_TEST_DB_PASSWORD" psql -h localhost -U "$TEST_ROLE" -d "$TEST_DB" -Atqc 'select 1' >/dev/null || die "Cannot log in to the test database."
  info "PostgreSQL listens on localhost only (Ubuntu default) - it is NOT exposed to the LAN."
  note "PostgreSQL databases '${DEV_DB}' (dev) and '${TEST_DB}' (tests); credentials in ${ENV_FILE} (mode 600)"
}

ensure_db() { # db role password
  local db=$1 role=$2 pass=$3
  psql_admin -v db="$db" -v role="$role" -v pass="$pass" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'role', :'pass')
  WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'role') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'role', :'pass') \gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'db', :'role')
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'db') \gexec
SQL
  info "database '${db}' (owner '${role}') ready"
}

setup_git() {
  log "Configuring git for GitHub collaboration"
  if [[ -n "${GIT_USER_NAME:-}" && -z "$(git config --global user.name || true)" ]]; then git config --global user.name "$GIT_USER_NAME"; fi
  if [[ -n "${GIT_USER_EMAIL:-}" && -z "$(git config --global user.email || true)" ]]; then git config --global user.email "$GIT_USER_EMAIL"; fi
  git config --global pull.ff only
  git config --global init.defaultBranch main
  if [[ -z "$(git config --global user.name || true)" || -z "$(git config --global user.email || true)" ]]; then
    warn "git identity not set. Run: git config --global user.name \"Your Name\" && git config --global user.email \"you@example.com\""
    note "ACTION: set your git identity (user.name / user.email) before committing"
  fi

  mkdir -p "$HOME/.ssh"; chmod 700 "$HOME/.ssh"
  if ! ls "$HOME"/.ssh/id_ed25519 "$HOME"/.ssh/id_rsa "$HOME"/.ssh/id_ecdsa >/dev/null 2>&1; then
    ssh-keygen -t ed25519 -N "" -C "${USER}@$(hostname)-atlas-dev" -f "$HOME/.ssh/id_ed25519" -q
    info "Generated a new SSH key (no passphrase; add one with: ssh-keygen -p -f ~/.ssh/id_ed25519)."
  fi
  SSH_PUBKEY_FILE=""
  local candidate
  for candidate in "$HOME/.ssh/id_ed25519.pub" "$HOME/.ssh/id_rsa.pub" "$HOME/.ssh/id_ecdsa.pub"; do
    if [[ -f "$candidate" ]]; then SSH_PUBKEY_FILE=$candidate; break; fi
  done
}

clone_or_update_repo() {
  log "Getting the code from GitHub"
  if [[ -d "$ATLAS_DIR/.git" ]]; then
    info "Repository already present at $ATLAS_DIR - updating"
    git -C "$ATLAS_DIR" fetch --prune origin
    if [[ -n "$(git -C "$ATLAS_DIR" status --porcelain)" ]]; then
      warn "Working tree has uncommitted changes - NOT pulling (your work is untouched). Commit/stash and re-run to sync."
      note "ACTION: working tree at $ATLAS_DIR is dirty; pull skipped"
    elif [[ "$(git -C "$ATLAS_DIR" rev-parse --abbrev-ref HEAD)" != "$ATLAS_BRANCH" ]]; then
      warn "Checked-out branch is '$(git -C "$ATLAS_DIR" rev-parse --abbrev-ref HEAD)', not '$ATLAS_BRANCH' - leaving it as is (no pull)."
    else
      git -C "$ATLAS_DIR" pull --ff-only origin "$ATLAS_BRANCH"
    fi
  else
    git clone --branch "$ATLAS_BRANCH" "$ATLAS_REPO_URL" "$ATLAS_DIR" ||
      die "Clone failed. If the repository is private, add the SSH key printed below to GitHub and re-run with ATLAS_REPO_URL=$ATLAS_REPO_SSH"
  fi

  # Fetch anonymously over HTTPS, push over SSH (key registered on GitHub), accept GitHub's host key on first use.
  git -C "$ATLAS_DIR" remote set-url --push origin "$ATLAS_REPO_SSH"
  git -C "$ATLAS_DIR" config core.sshCommand "ssh -o StrictHostKeyChecking=accept-new"
  info "HEAD: $(git -C "$ATLAS_DIR" log --oneline -1)"
  note "Repository: $ATLAS_DIR ($(git -C "$ATLAS_DIR" log --oneline -1))"
}

setup_ports_and_tls() {
  if [[ "${SKIP_PORTS:-0}" != "1" ]]; then
    log "Allowing unprivileged processes to bind ports 80/443"
    local current; current=$(cat /proc/sys/net/ipv4/ip_unprivileged_port_start)
    if (( current > 80 )); then
      echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-atlas-ports.conf >/dev/null
      sudo sysctl --system >/dev/null
      info "net.ipv4.ip_unprivileged_port_start=80 (was ${current})"
    else
      info "already ${current}"
    fi
  fi

  if [[ "${SKIP_TLS:-0}" == "1" ]]; then return 0; fi
  log "Creating a self-signed TLS certificate for the LAN"
  mkdir -p "$TLS_DIR"; chmod 700 "$TLS_DIR"
  local crt="$TLS_DIR/atlas.crt" key="$TLS_DIR/atlas.key" ip sans
  sans="DNS:localhost,DNS:$(hostname),IP:127.0.0.1"
  for ip in $(hostname -I 2>/dev/null); do
    if [[ $ip =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then sans+=",IP:${ip}"; fi
  done
  if [[ -n "${TLS_EXTRA_SANS:-}" ]]; then sans+=",${TLS_EXTRA_SANS}"; fi

  if [[ -f "$crt" && -f "$key" ]]; then
    info "Certificate already exists (delete $TLS_DIR to regenerate)."
    for ip in $(hostname -I 2>/dev/null); do
      if [[ $ip =~ ^[0-9.]+$ ]] && ! openssl x509 -in "$crt" -noout -ext subjectAltName 2>/dev/null | grep -q "$ip"; then
        warn "Certificate does not cover current address ${ip}; delete $TLS_DIR and re-run to include it."
      fi
    done
  else
    ( umask 077
      openssl req -x509 -newkey rsa:4096 -sha256 -days 365 -nodes -keyout "$key" -out "$crt" \
        -subj "/CN=$(hostname)" -addext "subjectAltName=${sans}" -addext "extendedKeyUsage=serverAuth" 2>/dev/null )
    info "Names in certificate: ${sans}"
  fi
  chmod 600 "$key"; chmod 644 "$crt"
  info "SHA-256 fingerprint: $(openssl x509 -in "$crt" -noout -fingerprint -sha256 | cut -d= -f2)"
  note "TLS certificate: $crt (self-signed; import it into clients that call this VM, or use curl --cacert)"
}

configure_secrets() {
  log "Restoring packages and configuring per-machine secrets (dotnet user-secrets)"
  cd "$ATLAS_DIR"
  dotnet restore Atlas.slnx

  local api=src/Atlas.API
  dotnet user-secrets set "ConnectionStrings:Atlas" "$DEV_CONNECTION" --project "$api" >/dev/null
  if dotnet user-secrets list --project "$api" 2>/dev/null | grep -q '^Jwt:SigningKey'; then
    info "Jwt:SigningKey already set - keeping it (existing tokens stay valid)"
  else
    dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project "$api" >/dev/null
    info "generated a new Jwt:SigningKey for THIS machine"
  fi
  if [[ "${SKIP_TLS:-0}" != "1" ]]; then
    dotnet user-secrets set "Kestrel:Certificates:Default:Path"    "$TLS_DIR/atlas.crt" --project "$api" >/dev/null
    dotnet user-secrets set "Kestrel:Certificates:Default:KeyPath" "$TLS_DIR/atlas.key" --project "$api" >/dev/null
  fi
  note "User-secrets set for src/Atlas.API (connection string, JWT signing key, Kestrel certificate) - stored under ~/.microsoft/usersecrets, never in Git"
}

build_and_migrate() {
  log "Building the solution"
  cd "$ATLAS_DIR"
  dotnet build Atlas.slnx --no-restore -c Debug

  log "Applying the committed EF Core migrations to the dev database"
  ConnectionStrings__Atlas="$DEV_CONNECTION" ASPNETCORE_ENVIRONMENT=Development \
    dotnet ef database update --no-build \
      --project src/Atlas.Infrastructure --startup-project src/Atlas.API
  note "Dev database '${DEV_DB}' is migrated to the latest committed migration"
}

bootstrap_admin() {
  if [[ "${BOOTSTRAP_ADMIN:-0}" != "1" ]]; then return 0; fi
  log "Creating the initial administrator (one-time)"
  cd "$ATLAS_DIR"
  local pw out
  pw="Tmp#$(random_hex 8)-9aZ"
  out=$(ConnectionStrings__Atlas="$DEV_CONNECTION" ASPNETCORE_ENVIRONMENT=Development Bootstrap__AdminPassword="$pw" \
        dotnet run --no-build --no-launch-profile --project src/Atlas.API -- --bootstrap-admin 2>&1) || { echo "$out" | tail -n 20; die "Bootstrap failed."; }
  if grep -q "Bootstrap complete" <<<"$out"; then
    note "Initial admin created. Username: admin   TEMPORARY password (shown once, must be changed at first login): $pw"
  else
    info "No admin created (one already exists)."
  fi
}

run_tests() {
  if [[ "${SKIP_TESTS:-0}" == "1" ]]; then warn "Skipping tests (SKIP_TESTS=1)"; return 0; fi
  log "Running the full test suite against the native test database"
  cd "$ATLAS_DIR"
  if dotnet test Atlas.slnx --no-build -c Debug; then
    TEST_STATUS="PASSED"
  else
    TEST_STATUS="FAILED"
  fi
  note "Tests: ${TEST_STATUS}"
}

print_summary() {
  local ip; ip=$(hostname -I 2>/dev/null | awk '{print $1}')
  log "Done"
  printf '\n'
  local line; for line in "${SUMMARY[@]}"; do printf '  - %s\n' "$line"; done

  if [[ -n "${SSH_PUBKEY_FILE:-}" ]]; then
    cat <<MSG

  To PUSH to GitHub from this VM, add this public key at https://github.com/settings/ssh/new
  (or as a write-enabled deploy key under the repository's Settings > Deploy keys):

$(sed 's/^/      /' "$SSH_PUBKEY_FILE")

  Then verify with:  ssh -T git@github.com
MSG
  fi

  cat <<MSG

  Open a NEW shell (or: source ~/.profile) so PATH and ATLAS_TEST_DB are loaded, then:

      cd ${ATLAS_DIR}
      ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Atlas.API      # HTTPS on 443, HTTP 80 -> 308 redirect
      # Swagger:  https://${ip:-<vm-ip>}/swagger      (self-signed certificate: trust ${TLS_DIR}/atlas.crt on the client)
      dotnet test Atlas.slnx                                                      # uses \$ATLAS_TEST_DB

  Firewall (if ufw is enabled), LAN only - adjust the CIDR:
      sudo ufw allow from 192.168.1.0/24 to any port 80,443 proto tcp

  Create the first admin (if you did not use BOOTSTRAP_ADMIN=1) - see README "First-run administrator bootstrap".
  Re-run this script any time after 'git pull' to re-sync: it migrates the dev DB, rebuilds and retests.
  Full log: ${LOG_FILE}
MSG
}

main() {
  ( umask 077; touch "$LOG_FILE" ); chmod 600 "$LOG_FILE"   # the log can contain the one-time admin password
  exec > >(tee -a "$LOG_FILE") 2>&1
  trap 'printf "\n\033[1;31mFAILED\033[0m at line %s: %s\n(see %s)\n" "$LINENO" "$BASH_COMMAND" "$LOG_FILE" >&2' ERR
  printf '\n######## Atlas dev VM setup - %s ########\n' "$(date -Is)"

  preflight
  install_packages
  install_dotnet
  setup_postgres
  setup_git
  clone_or_update_repo
  setup_ports_and_tls
  configure_secrets
  build_and_migrate
  bootstrap_admin
  TEST_STATUS="SKIPPED"; run_tests
  print_summary
  [[ "$TEST_STATUS" != "FAILED" ]]
}

# Allow `source setup-dev-vm.sh` (for testing the helpers) without running anything.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then main "$@"; fi
