#!/usr/bin/env bash
set -euo pipefail

psql --username "${POSTGRES_USER:-postgres}" --dbname "${POSTGRES_DB:-postgres}" --set ON_ERROR_STOP=1 <<'SQL'
CREATE DATABASE reservations OWNER program;
CREATE DATABASE payments OWNER program;
CREATE DATABASE loyalties OWNER program;
SQL
